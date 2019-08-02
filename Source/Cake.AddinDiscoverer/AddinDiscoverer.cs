using Cake.AddinDiscoverer.Steps;
using Cake.AddinDiscoverer.Utilities;
using Newtonsoft.Json.Linq;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using Octokit;
using Octokit.Internal;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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

			// Discover all existing Cake addins on NuGet
			typeof(DiscoveryStep),

			// Remove blacklisted addins
			typeof(BlacklistStep),

			// Sanity check on the list of addins we discovered
			typeof(ValidateDiscoveryStep),

			// Download the packages from NuGet if they are not already in the cache
			typeof(DownloadStep),

			// Analyze the metadata in the downloaded nuget package
			typeof(AnalyzeNuGetMetadataStep),

			// GEt the owners of the NuGet package
			typeof(GetPackageOwnershipStep),

			// Some addins were moved to the cake-contrib organization but the URL in their package metadata still
			// points to the original repo. This step corrects the URL to ensure it points to the right repo.
			// Also, this step forces HTTPS for github URLs.
			typeof(ValidateUrlStep),

			// Use the info from previous steps to determine if addins meet the best pratices
			typeof(AnalyzeAddinsStep),

			// Get previously created issue and pull request from the Github repo
			typeof(GetGithubIssuesStep),

			// Get statistics from the Github repo
			typeof(GetGithubStatsStep),

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
			typeof(UpdateCakeRecipeStep)
		};

		private readonly DiscoveryContext _context;

		public AddinDiscoverer(Options options)
		{
			// Setup the Github client
			var proxy = string.IsNullOrEmpty(options.ProxyUrl) ? null : new WebProxy(options.ProxyUrl);
			var credentials = new Credentials(options.GithubUsername, options.GithuPassword);
			var connection = new Connection(new ProductHeaderValue(Constants.PRODUCT_NAME), new HttpClientAdapter(() => HttpMessageHandlerFactory.CreateDefault(proxy)))
			{
				Credentials = credentials,
			};

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
				NugetRepository = new SourceRepository(packageSource, providers),
				Options = options,
				TempFolder = Path.Combine(options.TemporaryFolder, Constants.PRODUCT_NAME),
				Version = typeof(AddinDiscoverer).GetTypeInfo().Assembly.GetName().Version.ToString(3)
			};

			// Using '.CodeBase' because it returns where the assembly is located when not executing (in other words, the 'permanent' path of the assembly).
			// '.Location' would seem more intuitive but in the case of shadow copyied assemblies, it would return a path in a temp directory.
			var currentPath = new Uri(System.Reflection.Assembly.GetExecutingAssembly().CodeBase).LocalPath;
			var currentFolder = Path.GetDirectoryName(currentPath);
			var blacklistFilePath = Path.Combine(currentFolder, "blacklist.json");

			using (var sr = new StreamReader(blacklistFilePath))
			{
				var json = sr.ReadToEnd();
				var jObject = JObject.Parse(json);

				_context.BlacklistedAddins = jObject.Property("packages").Value.ToObject<string[]>();
				_context.BlacklistedTags = jObject.Property("labels").Value.ToObject<string[]>();
			}
		}

		public async Task LaunchDiscoveryAsync()
		{
			try
			{
				foreach (var type in _steps)
				{
					var step = (IStep)Activator.CreateInstance(type);

					if (step.PreConditionIsMet(_context))
					{
						Console.WriteLine(step.GetDescription(_context));
						await step.ExecuteAsync(_context).ConfigureAwait(false);
					}
				}
			}
			catch (Exception e)
			{
				Console.WriteLine($"{Environment.NewLine}***** AN EXCEPTION HAS OCCURED *****");
				Console.WriteLine(e.Demystify().ToString());
			}
		}
	}
}
