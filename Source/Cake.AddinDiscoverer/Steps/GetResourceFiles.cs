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
			var exclusionListAsJObject = await GetResourceFile(context, "exclusionlist.json").ConfigureAwait(false);
			var inclusionListAsJObject = await GetResourceFile(context, "inclusionlist.json").ConfigureAwait(false);

			context.ExcludedAddins = exclusionListAsJObject.Property("packages")?.Value.ToObject<string[]>() ?? Array.Empty<string>();
			context.ExcludedTags = exclusionListAsJObject.Property("labels")?.Value.ToObject<string[]>() ?? Array.Empty<string>();
			context.IncludedAddins = inclusionListAsJObject.Property("packages")?.Value.ToObject<string[]>() ?? Array.Empty<string>();
		}

		private Task<JObject> GetResourceFile(DiscoveryContext context, string resourceName)
		{
			if (context.Options.UseLocalResources)
			{
				return Task.FromResult<JObject>(GetLocalResource(resourceName));
			}
			else
			{
				return DownloadResourceFromGitHub(context, resourceName);
			}
		}

		private async Task<JObject> DownloadResourceFromGitHub(DiscoveryContext context, string resourceName)
		{
			var resourcePath = $"Source/Cake.AddinDiscoverer/{resourceName}";
			var contents = await context.GithubClient.Repository.Content.GetAllContents(Constants.CAKE_CONTRIB_REPO_OWNER, Constants.ADDIN_DISCOVERER_REPO_NAME, resourcePath).ConfigureAwait(false);

			var jObject = JObject.Parse(contents[0].Content);

			return jObject;
		}

		private JObject GetLocalResource(string resourceName)
		{
			// Using '.CodeBase' because it returns where the assembly is located when not executing (in other words, the 'permanent' path of the assembly).
			// '.Location' would seem more intuitive but in the case of shadow copied assemblies, it would return a path in a temp directory.
			var currentPath = new Uri(Assembly.GetExecutingAssembly().CodeBase).LocalPath;
			var currentFolder = Path.GetDirectoryName(currentPath);
			var filePath = Path.Combine(currentFolder, resourceName);

			JObject jObject;
			using (var sr = new StreamReader(filePath))
			{
				var json = sr.ReadToEnd();
				jObject = JObject.Parse(json);
			}

			return jObject;
		}
	}
}
