using Cake.AddinDiscoverer.Models;
using System.IO;
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
			// Save file
			var jsonFileName = "Analysis_result.json";
			var jsonFilePath = Path.Combine(context.TempFolder, jsonFileName);
			using FileStream jsonFileStream = File.Create(jsonFilePath);
			await JsonSerializer.SerializeAsync(jsonFileStream, context.Addins, context.Addins.GetType(), new JsonSerializerOptions { WriteIndented = false }, cancellationToken).ConfigureAwait(false);

			// Clear the temporary files
			Directory.Delete(context.AnalysisFolder, true);
		}
	}
}
