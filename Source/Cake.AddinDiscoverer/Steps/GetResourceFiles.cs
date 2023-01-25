using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class GetResourceFiles : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => true;

		public string GetDescription(DiscoveryContext context) => "Download resource files";

		public async Task ExecuteAsync(DiscoveryContext context, TextWriter log, CancellationToken cancellationToken)
		{
			// Load exclusion list
			var exclusionListContent = await GetResourceFileContentAsync(context, "exclusionlist.json").ConfigureAwait(false);
			var exclusionListAsJObject = JObject.Parse(exclusionListContent);
			context.ExcludedAddins = exclusionListAsJObject.Property("packages")?.Value.ToObject<string[]>() ?? Array.Empty<string>();
			context.ExcludedTags = exclusionListAsJObject.Property("labels")?.Value.ToObject<string[]>() ?? Array.Empty<string>();

			// Load inclusion list
			var inclusionListContent = await GetResourceFileContentAsync(context, "inclusionlist.json").ConfigureAwait(false);
			var inclusionListAsJObject = JObject.Parse(inclusionListContent);
			context.IncludedAddins = inclusionListAsJObject.Property("packages")?.Value.ToObject<string[]>() ?? Array.Empty<string>();

			// Load the result of previous analysis
			var previousAnalysisContent = await GetResourceFileContentAsync(context, "Analysis_result.json").ConfigureAwait(false);
			if (!string.IsNullOrEmpty(previousAnalysisContent))
			{
				context.Addins = JsonSerializer.Deserialize<AddinMetadata[]>(previousAnalysisContent, default(JsonSerializerOptions));
			}

			// Load individual analysis results (which are present if the previous analysis was interrupted)
			var deserializedAddins = Directory.GetFiles(context.AnalysisFolder, "*.json")
				.Select(fileName =>
				{
					using FileStream fileStream = File.OpenRead(fileName);
					var deserializedAddin = JsonSerializer.Deserialize<AddinMetadata>(fileStream);
					return deserializedAddin;
				})
				.ToArray();

			if (deserializedAddins.Length > 0)
			{
				var addinsList = context.Addins.ToList();
				addinsList.RemoveAll(addin => deserializedAddins.Any(d => d.Name.EqualsIgnoreCase(addin.Name) && d.NuGetPackageVersion == addin.NuGetPackageVersion));
				addinsList.AddRange(deserializedAddins);
				context.Addins = addinsList.ToArray();
			}

			// Filter the addins if necessary
			if (!string.IsNullOrEmpty(context.Options.AddinName))
			{
				context.Addins = context.Addins.Where(addin => addin.Name.EqualsIgnoreCase(context.Options.AddinName)).ToArray();
			}
		}

		private static Task<string> GetResourceFileContentAsync(DiscoveryContext context, string resourceName)
		{
			if (context.Options.UseLocalResources)
			{
				return GetLocalResourceContent(resourceName);
			}
			else
			{
				return DownloadResourceContentFromGitHubAsync(context, resourceName);
			}
		}

		private static async Task<string> DownloadResourceContentFromGitHubAsync(DiscoveryContext context, string resourceName)
		{
			try
			{
				var resourcePath = $"Source/Cake.AddinDiscoverer/{resourceName}";
				var contents = await context.GithubClient.Repository.Content.GetAllContents(Constants.CAKE_CONTRIB_REPO_OWNER, Constants.ADDIN_DISCOVERER_REPO_NAME, resourcePath).ConfigureAwait(false);
				return contents[0].Content;
			}
			catch
			{
				return null;
			}
		}

		private static async Task<string> GetLocalResourceContent(string resourceName)
		{
			try
			{
				var currentPath = new Uri(Assembly.GetExecutingAssembly().Location).LocalPath;
				var currentFolder = Path.GetDirectoryName(currentPath);
				var filePath = Path.Combine(currentFolder, resourceName);

				using (var sr = new StreamReader(filePath))
				{
					return await sr.ReadToEndAsync().ConfigureAwait(false);
				}
			}
			catch
			{
				return null;
			}
		}
	}
}
