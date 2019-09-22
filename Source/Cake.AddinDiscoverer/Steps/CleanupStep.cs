using Cake.AddinDiscoverer.Models;
using System.IO;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class CleanupStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => true;

		public string GetDescription(DiscoveryContext context) => $"Clean up {context.TempFolder}";

		public async Task ExecuteAsync(DiscoveryContext context)
		{
			if (context.Options.ClearCache && Directory.Exists(context.TempFolder))
			{
				Directory.Delete(context.TempFolder, true);
				await Task.Delay(500).ConfigureAwait(false);
			}

			if (!Directory.Exists(context.TempFolder))
			{
				Directory.CreateDirectory(context.TempFolder);
				await Task.Delay(500).ConfigureAwait(false);
			}

			if (!Directory.Exists(context.PackagesFolder))
			{
				Directory.CreateDirectory(context.PackagesFolder);
				await Task.Delay(500).ConfigureAwait(false);
			}

			if (File.Exists(context.ExcelReportPath)) File.Delete(context.ExcelReportPath);
			if (File.Exists(context.MarkdownReportPath)) File.Delete(context.MarkdownReportPath);
			if (File.Exists(context.StatsSaveLocation)) File.Delete(context.StatsSaveLocation);
			if (File.Exists(context.GraphSaveLocation)) File.Delete(context.GraphSaveLocation);

			foreach (var markdownReport in Directory.EnumerateFiles(context.TempFolder, $"{Path.GetFileNameWithoutExtension(context.MarkdownReportPath)}*.md"))
			{
				File.Delete(markdownReport);
			}
		}
	}
}
