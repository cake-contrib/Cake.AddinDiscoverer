using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class LoadPreviousAnalysisStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => true;

		public string GetDescription(DiscoveryContext context) => "Load the result of previous analysis";

		public async Task ExecuteAsync(DiscoveryContext context, TextWriter log, CancellationToken cancellationToken)
		{
			if (context.Options.AnalyzeAllAddins)
			{
				context.Addins = Array.Empty<AddinMetadata>();
				return;
			}

			string previousAnalysisContent;

			if (File.Exists(context.AnalysisResultSaveLocation))
			{
				previousAnalysisContent = await File.ReadAllTextAsync(context.AnalysisResultSaveLocation, cancellationToken).ConfigureAwait(false);
			}
			else
			{
				// Load the result of previous analysis
				var contents = await context.GithubClient.Repository.Content.GetAllContents(Constants.CAKE_CONTRIB_REPO_OWNER, Constants.CAKE_CONTRIB_REPO_NAME, Path.GetFileName(context.AnalysisResultSaveLocation)).ConfigureAwait(false);

				// The file is too large to be retrieved from the GitHub API. We must issue a HTTP GET to the download URL
				previousAnalysisContent = await context.HttpClient.GetStringAsync(contents[0].DownloadUrl).ConfigureAwait(false);
			}

			// Deseralize the content of the previous analysis
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
		}
	}
}
