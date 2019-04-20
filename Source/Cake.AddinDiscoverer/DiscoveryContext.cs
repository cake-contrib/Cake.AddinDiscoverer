using NuGet.Protocol.Core.Types;
using Octokit;
using System.IO;

namespace Cake.AddinDiscoverer
{
	internal class DiscoveryContext
	{
		public Options Options { get; set; }

		public string TempFolder { get; set; }

		public IGitHubClient GithubClient { get; set; }

		public SourceRepository NugetRepository { get; set; }

		public string Version { get; set; }

		public AddinMetadata[] Addins { get; set; }

		/// <summary>
		/// Gets or sets the list of addins that we specifically want to exclude from our reports
		/// </summary>
		public string[] BlacklistedAddins { get; set; }

		/// <summary>
		/// Gets or sets the list of tags to be filtered out when generating an addin's YAML
		/// </summary>
		public string[] BlacklistedTags { get; set; }

		public string PackagesFolder => Path.Combine(this.TempFolder, "packages");

		public string ExcelReportPath => Path.Combine(this.TempFolder, "Audit.xlsx");

		public string MarkdownReportPath => Path.Combine(this.TempFolder, "Audit.md");

		public string StatsSaveLocation => Path.Combine(this.TempFolder, "Audit_stats.csv");

		public string GraphSaveLocation => Path.Combine(this.TempFolder, "Audit_progress.png");
	}
}
