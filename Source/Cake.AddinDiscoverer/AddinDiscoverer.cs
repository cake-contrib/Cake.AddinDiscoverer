using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Steps;
using Cake.AddinDiscoverer.Utilities;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;
using Octokit;
using Octokit.Internal;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer
{
	internal class AddinDiscoverer
	{
		// These are the steps that will be executed by the Addin.Discoverer (if their pre-condition is met).
		// The order is important!
		private readonly Type[] _steps = new[]
		{
			// Delete artifacts from previous audits
			typeof(CleanupStep),

			// Download resource files (such as exclusion list and inclusion list)
			typeof(GetResourceFiles),

			// Discover all existing Cake addins on NuGet
			typeof(DiscoveryStep),

			// Remove excluded addins
			typeof(ExclusionlistStep),

			// Sanity check on the list of addins we discovered
			typeof(ValidateDiscoveryStep),

			// Download the packages from NuGet if they are not already in the cache
			typeof(DownloadStep),

			// Analyze the metadata in the downloaded nuget package
			typeof(AnalyzeNuGetMetadataStep),

			// Get the owners of the NuGet package
			typeof(GetPackageOwnershipStep),

			// Some addins were moved to the cake-contrib organization but the URL in their package metadata still
			// points to the original repo. This step corrects the URL to ensure it points to the right repo.
			// Also, this step forces HTTPS for github URLs.
			typeof(ValidateUrlStep),

			// Use the info from previous steps to determine if addins meet the best practices
			typeof(AnalyzeAddinsStep),

			// Get previously created issue and pull request from the Github repo
			typeof(GetGithubIssuesStep),

			// Get statistics, content, etc. from the Github repo
			typeof(GetGithubMetadataStep),

			// Check if addins are using Cake.Recipe
			typeof(CheckUsingCakeRecipeStep),

			// Generate an Excel spreadsheet with the result of the audit
			typeof(GenerateExcelReportStep),

			// Generate a markdown file with the result of the audit
			typeof(GenerateMarkdownReportStep),

			// Update the CSV file with statistics about the audit
			typeof(UpdateStatsCsvStep),

			// Generate a graph to percentage of addins that meet best practices over time
			typeof(GenerateStatsGraphStep),

			// Commit the artifacts (such as Excel and markdown reports, CSV, etc.) to the cake-contrib/home github repo
			typeof(CommitToRepoStep),

			// Create an issue to inform addin authors of the issues we discovered
			typeof(CreateGithubIssueStep),

			// Submit a pull request to fix the issue we discovered
			typeof(SubmitGithubPullRequest),

			// Make sure the YAML files in the cake-build/website repo are up to date
			// These files are used to generate the addins documentation published on Cake's web site
			typeof(SynchronizeYamlStep),

			// Update the addin references in the Cake.Recipe. Also, upgrade the version of Cake used to build
			// Cake.Recipe IF AND ONLY IF all references have been updated to be compatible with the latest version
			typeof(UpdateCakeRecipeStep),

			// Get the list of people who contributed to Cake
			typeof(GetContributorsStep)
		};

		private readonly DiscoveryContext _context;

		public AddinDiscoverer(Options options)
		{
			// Setup the Github client
			var proxy = string.IsNullOrEmpty(options.ProxyUrl) ? null : new WebProxy(options.ProxyUrl);
			var credentials = !string.IsNullOrEmpty(options.GithubToken) ? new Credentials(options.GithubToken) : new Credentials(options.GithubUsername, options.GithuPassword);
			var connection = new Connection(new ProductHeaderValue(Constants.PRODUCT_NAME), new HttpClientAdapter(() => HttpMessageHandlerFactory.CreateDefault(proxy)))
			{
				Credentials = credentials,
			};

			// This environment variable is used by NuGet.Configuration.ProxyCache to configure a proxy
			Environment.SetEnvironmentVariable("http_proxy", options.ProxyUrl);

			// Setup nuget
			var providers = new List<Lazy<INuGetResourceProvider>>();
			providers.AddRange(NuGet.Protocol.Core.Types.Repository.Provider.GetCoreV3());  // Add v3 API support
			var packageSource = new PackageSource("https://api.nuget.org/v3/index.json");

			// Setup the context that will be passed to each step
			_context = new DiscoveryContext()
			{
				Addins = Array.Empty<AddinMetadata>(),
				GithubClient = new GitHubClient(connection),
				GithubHttpClient = new HttpClientAdapter(() => HttpMessageHandlerFactory.CreateDefault(proxy)),
				HttpClient = new HttpClient(new HttpClientHandler() { Proxy = proxy, UseProxy = proxy != null }),
				NugetRepository = new SourceRepository(packageSource, providers),
				Options = options,
				TempFolder = Path.Combine(options.TemporaryFolder, Constants.PRODUCT_NAME),
				Version = typeof(AddinDiscoverer).GetTypeInfo().Assembly.GetName().Version.ToString(3)
			};
		}

		public async Task<ResultCode> LaunchDiscoveryAsync()
		{
			var cts = new CancellationTokenSource();
			var cancellationToken = cts.Token;

			var originalConsoleColor = Console.ForegroundColor;
			var result = ResultCode.Success;

			var maxStepNameLength = 60;
			var lineFormat = "{0,-" + maxStepNameLength + "}{1,-20}";
			Console.ForegroundColor = ConsoleColor.Green;

			Console.WriteLine();
			Console.WriteLine(lineFormat, "Step", "Duration");
			Console.WriteLine(new string('-', 20 + maxStepNameLength));

			var totalTime = Stopwatch.StartNew();

			foreach (var type in _steps)
			{
				var step = (IStep)Activator.CreateInstance(type);
				var stepDescription = step.GetDescription(_context);
				if (stepDescription.Length > maxStepNameLength - 1)
				{
					stepDescription = stepDescription.Substring(0, maxStepNameLength - 1);
				}

				if (cancellationToken.IsCancellationRequested)
				{
					Console.ForegroundColor = ConsoleColor.Red;
					Console.WriteLine(lineFormat, stepDescription, "Skipped due to cancellation");
				}
				else if (step.PreConditionIsMet(_context))
				{
					try
					{
						var log = new StringWriter();
						var stepDuration = Stopwatch.StartNew();
						await step.ExecuteAsync(_context, log, cancellationToken).ConfigureAwait(false);
						stepDuration.Stop();

						Console.ForegroundColor = ConsoleColor.Green;
						Console.WriteLine(lineFormat, stepDescription, stepDuration.Elapsed.ToString("c", CultureInfo.InvariantCulture));
						WriteLog(log.ToString());
					}
					catch (Exception e)
					{
						Console.ForegroundColor = originalConsoleColor;
						Console.WriteLine($"{Environment.NewLine}***** AN EXCEPTION HAS OCCURED *****");
						Console.WriteLine($"***** {stepDescription} *****");
						Console.WriteLine(e.Demystify().ToString());
						Console.WriteLine();
						result = ResultCode.Error;
						cts.Cancel();
					}
				}
				else
				{
					Console.ForegroundColor = ConsoleColor.Gray;
					Console.WriteLine(lineFormat, stepDescription, "Skipped");
				}
			}

			totalTime.Stop();

			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine(new string('-', 20 + maxStepNameLength));
			Console.WriteLine(lineFormat, "Total:", totalTime.Elapsed.ToString("c", CultureInfo.InvariantCulture));

			// Reset the foreground color
			Console.ForegroundColor = originalConsoleColor;

			return result;
		}

		private static void WriteLog(string log)
		{
			if (string.IsNullOrEmpty(log)) return;

			var lines = log.EndsWith(Environment.NewLine)
				? log.Substring(0, log.Length - Environment.NewLine.Length).Split(Environment.NewLine)
				: log.Split(Environment.NewLine);

			foreach (var line in lines)
			{
				Console.WriteLine("\t" + line);
			}
		}
	}
}
