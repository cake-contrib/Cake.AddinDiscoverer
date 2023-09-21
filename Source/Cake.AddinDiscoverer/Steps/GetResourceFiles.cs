using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Reflection;
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
			context.ExcludedContributors = exclusionListAsJObject.Property("contributors")?.Value.ToObject<string[]>() ?? Array.Empty<string>();
			context.ExcludedRepositories = exclusionListAsJObject.Property("repositories")?.Value.ToObject<string[]>() ?? Array.Empty<string>();

			// Load inclusion list
			var inclusionListContent = await GetResourceFileContentAsync(context, "inclusionlist.json").ConfigureAwait(false);
			var inclusionListAsJObject = JObject.Parse(inclusionListContent);
			context.IncludedAddins = inclusionListAsJObject.Property("packages")?.Value.ToObject<string[]>() ?? Array.Empty<string>();
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
