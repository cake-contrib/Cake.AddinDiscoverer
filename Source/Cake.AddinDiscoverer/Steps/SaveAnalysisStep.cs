using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using System;
using System.IO;
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

			// Serialize
			var sb = new StringBuilder();
			sb.AppendLine("[");
			sb.AppendJoin(',', context.Addins.Select(addinMetadata => $"\t{Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(addinMetadata, jsonOptions))}{Environment.NewLine}"));
			sb.AppendLine("]");

			// Save file
			await File.WriteAllTextAsync(context.AnalysisResultSaveLocation, sb.ToString(), cancellationToken).ConfigureAwait(false);

			// Clear the temporary files
			Directory.Delete(context.AnalysisFolder, true);
		}
	}
}
