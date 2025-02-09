using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
using Octokit;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
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
				// Load the result of previous analysis

				// Please note that the analysis result file is compressed since February 2025 because it has gotten
				// too large for Octokit resulting in the following exception:
				// Octokit.ApiValidationException: Sorry, your input was too large to process.Consider creating the blob in a local clone of the repository and then pushing it to GitHub.

				// First,  try to load previous analysis result from a ZIP on the local machine (when AddinDisco is running locally)
				if (File.Exists(context.CompressedAnalysisResultSaveLocation))
				{
					using (var zipFile = ZipFile.OpenRead(context.CompressedAnalysisResultSaveLocation))
					{
						previousAnalysisContent = new string(
							new StreamReader(
								zipFile.Entries
									.Where(x => x.Name.Equals(Path.GetFileName(context.AnalysisResultSaveLocation), StringComparison.InvariantCulture))
									.FirstOrDefault()
									.Open(),
								Encoding.UTF8)
							.ReadToEnd()
							.ToArray());
					}
				}

				// Second, try to load previous analysis result from a JSON on the local machine (when AddinDisco is running locally)
				else if (File.Exists(context.AnalysisResultSaveLocation))
				{
					previousAnalysisContent = await File.ReadAllTextAsync(context.AnalysisResultSaveLocation, cancellationToken).ConfigureAwait(false);
				}

				// Third, try to load previous analysis result from a ZIP from the GitHub repo
				else
				{
					try
					{
						var contents = await context.GithubClient.Repository.Content.GetAllContents(Constants.CAKE_CONTRIB_REPO_OWNER, Constants.CAKE_CONTRIB_REPO_NAME, Path.GetFileName(context.CompressedAnalysisResultSaveLocation)).ConfigureAwait(false);

						// The ZIP file is probably small enough to be retrieved using Octokit, but just to be safe let's issue a HTTP GET to the download URL
						var zippedContent = await context.HttpClient.GetByteArrayAsync(contents[0].DownloadUrl, cancellationToken).ConfigureAwait(false);

						// Unzip the file
						var zipFile = new ZipArchive(new MemoryStream(zippedContent));

						previousAnalysisContent = new string(
							new StreamReader(
								zipFile.Entries
									.Where(x => x.Name.Equals(Path.GetFileName(context.AnalysisResultSaveLocation), StringComparison.InvariantCulture))
									.FirstOrDefault()
									.Open(),
								Encoding.UTF8)
							.ReadToEnd()
							.ToArray());
					}
					catch (NotFoundException)
					{
						// When all else fails, try to load previous analysis result from a JSON from the GitHub repo
						var contents = await context.GithubClient.Repository.Content.GetAllContents(Constants.CAKE_CONTRIB_REPO_OWNER, Constants.CAKE_CONTRIB_REPO_NAME, Path.GetFileName(context.AnalysisResultSaveLocation)).ConfigureAwait(false);

						// The file is too large to be retrieved from the GitHub API. We must issue a HTTP GET to the download URL
						previousAnalysisContent = await context.HttpClient.GetStringAsync(contents[0].DownloadUrl, cancellationToken).ConfigureAwait(false);
					}
				}
			}

			// Deseralize the content of the previous analysis
			if (!string.IsNullOrEmpty(previousAnalysisContent))
			{
				context.Addins = JsonSerializer.Deserialize<AddinMetadata[]>(previousAnalysisContent, Misc.GetJsonOptions(true));
			}

			// Load individual analysis results (which are present if the previous analysis was interrupted)
			var deserializedAddins = Directory.GetFiles(context.AnalysisFolder, "*.json")
				.Select(fileName =>
				{
					using FileStream fileStream = File.OpenRead(fileName);
					var deserializedAddin = JsonSerializer.Deserialize<AddinMetadata>(fileStream, Misc.GetJsonOptions(true));
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
