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
		private readonly Type[] _steps = new[]
		{
			typeof(CleanupStep),
			typeof(DiscoveryStep),
			typeof(BlacklistStep),
			typeof(ValidateDiscoveryStep),
			typeof(ValidateUrlStep),
			typeof(DownloadStep),
			typeof(FindGithubIssueStep),
			typeof(AnalyzeNuGetMetadataStep),
			typeof(AnalyzeAddinsStep),
			typeof(GenerateExcelReportStep),
			typeof(GenerateMarkdownReportStep),
			typeof(UpdateStatsCsvStep),
			typeof(GenerateStatsGraphStep),
			typeof(CommitToRepoStep),
			typeof(CreateGithubIssueStep),
			typeof(SynchronizeYamlStep),
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

			// Setup the context that will be passed to the tasks
			_context = new DiscoveryContext()
			{
				Addins = Array.Empty<AddinMetadata>(),
				GithubClient = new GitHubClient(connection),
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
