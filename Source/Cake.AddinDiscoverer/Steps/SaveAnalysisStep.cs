using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
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
	internal class SaveAnalysisStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => true;

		public string GetDescription(DiscoveryContext context) => "Save the result of the analysis";

		public async Task ExecuteAsync(DiscoveryContext context, TextWriter log, CancellationToken cancellationToken)
		{
			var jsonOptions = Misc.GetJsonOptions(false);

			// Serialize to file
			// Please note: since February 2025 this file is compressed because the JSON has become too large for Octokit
			using (var zipFile = new FileStream(context.CompressedAnalysisResultSaveLocation, FileMode.OpenOrCreate))
			{
				using (var archive = new ZipArchive(zipFile, ZipArchiveMode.Update))
				{
					var entryName = Path.GetFileName(context.AnalysisResultSaveLocation);

					// Under normal circumstances there should only be one JSON file that needs to be deleted.
					// But if something goes wrong during a debugging session, there's a possibility that multiple
					// JSON files with the same name might end up in the ZIP. I know this because it has happened
					// to me while debugging on my laptop. The following loop is a safeguard against that possibility:
					var previousEntries = archive.Entries.Where(entry => entry.Name == entryName).ToArray();
					foreach (var previousEntry in previousEntries ?? Array.Empty<ZipArchiveEntry>())
					{
						previousEntry.Delete();
					}

					var analysisEntry = archive.CreateEntry(entryName);
					using (StreamWriter writer = new StreamWriter(analysisEntry.Open()))
					{
						await writer.WriteLineAsync("[").ConfigureAwait(false);
						await writer.WriteJoinAsync(",", context.Addins.Select(addinMetadata => $"\t{Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(addinMetadata, jsonOptions))}{Environment.NewLine}"));
						await writer.WriteLineAsync("]").ConfigureAwait(false);
					}
				}
			}

			// Clear the temporary files
			Directory.Delete(context.AnalysisFolder, true);
		}
	}
}
