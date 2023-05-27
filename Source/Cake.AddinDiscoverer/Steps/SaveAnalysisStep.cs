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

		public Task ExecuteAsync(DiscoveryContext context, TextWriter log, CancellationToken cancellationToken)
		{
			// Save file
			using FileStream jsonFileStream = File.Create(context.AnalysisResultSaveLocation);
			JsonSerializer.Serialize(jsonFileStream, context.Addins, typeof(AddinMetadata[]), new JsonSerializerOptions { WriteIndented = true });

			// Clear the temporary files
			Directory.Delete(context.AnalysisFolder, true);

			return Task.CompletedTask;
		}
	}
}
