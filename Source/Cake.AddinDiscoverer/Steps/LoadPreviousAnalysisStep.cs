using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
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
			var previousAnalysisContent = string.Empty;

			if (!context.Options.AnalyzeAllAddins)
			{
				if (File.Exists(context.AnalysisResultSaveLocation))
				{
					previousAnalysisContent = await File.ReadAllTextAsync(context.AnalysisResultSaveLocation, cancellationToken).ConfigureAwait(false);
				}
				else
				{
					// Load the result of previous analysis
					var contents = await context.GithubClient.Repository.Content.GetAllContents(Constants.CAKE_CONTRIB_REPO_OWNER, Constants.CAKE_CONTRIB_REPO_NAME, Path.GetFileName(context.AnalysisResultSaveLocation)).ConfigureAwait(false);

					// The file is too large to be retrieved from the GitHub API. We must issue a HTTP GET to the download URL
					previousAnalysisContent = await context.HttpClient.GetStringAsync(contents[0].DownloadUrl, cancellationToken).ConfigureAwait(false);
				}
			}

			// Deserialize the content of the previous analysis
			if (!string.IsNullOrEmpty(previousAnalysisContent))
			{
				var addinVersions = JsonSerializer.Deserialize<AddinVersionMetadata[]>(previousAnalysisContent, Misc.GetJsonOptions(true));
				context.Addins = addinVersions
					.GroupBy(addinVersion => addinVersion.Name)
					.ToDictionary(
						grp => new AddinMetadata() { Name = grp.Key },
						grp => grp.ToArray());
			}

			// Load individual analysis results (which are present if the previous analysis was interrupted)
			var deserializedAddins = Directory.GetFiles(context.AnalysisFolder, "*.json")
				.Select(fileName =>
				{
					using FileStream fileStream = File.OpenRead(fileName);
					var deserializedAddin = JsonSerializer.Deserialize<AddinVersionMetadata>(fileStream, Misc.GetJsonOptions(true));
					return deserializedAddin;
				})
				.ToArray();

			if (deserializedAddins.Length > 0)
			{
				foreach (var addin in deserializedAddins)
				{
					if (context.Addins.TryGetAddinByName(addin.Name, out (AddinMetadata Addin, AddinVersionMetadata[] AddinVersions) addinInfo))
					{
						addinInfo.AddinVersions = addinInfo.AddinVersions.
							ToList()
							.Where(addinVersion => addinVersion.NuGetPackageVersion != addin.NuGetPackageVersion)
							.Union(new[] { addin })
							.ToArray();
					}
					else
					{
						context.Addins.Add(new AddinMetadata() { Name = addin.Name }, new[] { addin });
					}
				}
			}
		}
	}
}
