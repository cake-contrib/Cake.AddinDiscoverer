using Cake.AddinDiscoverer.Models;
using System;
using System.IO;
using System.Reflection;
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
			var currentPath = new Uri(Assembly.GetExecutingAssembly().Location).LocalPath;
			var currentFolder = Path.GetDirectoryName(currentPath);
			var jsonFilePath = Path.Combine(currentFolder, "Analysis_result.json");
			using FileStream jsonFileStream = File.Create(jsonFilePath);
			await JsonSerializer.SerializeAsync(jsonFileStream, context.Addins, typeof(AddinMetadata[]), new JsonSerializerOptions { WriteIndented = true }, cancellationToken).ConfigureAwait(false);

			// Clear the temporary files
			Directory.Delete(context.AnalysisFolder, true);
		}
	}
}
