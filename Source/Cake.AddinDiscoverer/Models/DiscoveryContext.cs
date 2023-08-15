using NuGet.Protocol.Core.Types;
using Octokit;
using Octokit.Internal;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;

namespace Cake.AddinDiscoverer.Models
{
	internal class DiscoveryContext
	{
		public Options Options { get; set; }

		public string TempFolder { get; set; }

		public IGitHubClient GithubClient { get; set; }

		public IHttpClient GithubHttpClient { get; set; }

		public HttpClient HttpClient { get; set; }

		public SourceRepository NugetRepository { get; set; }

		public string Version { get; set; }

		public AddinMetadata[] Addins { get; set; }

		/// <summary>
		/// Gets or sets the list of addins that we specifically want to include in our reports
		/// (in addition to all addins matching the naming convention).
		/// </summary>
		public string[] IncludedAddins { get; set; }

		/// <summary>
		/// Gets or sets the list of addins that we specifically want to exclude from our reports.
		/// </summary>
		public string[] ExcludedAddins { get; set; }

		/// <summary>
		/// Gets or sets the list of tags to be filtered out when generating an addin's YAML.
		/// </summary>
		public string[] ExcludedTags { get; set; }

		/// <summary>
		/// Gets or sets the list of GitHub users to be filtered out when generating the list of contributors.
		/// </summary>
		public string[] ExcludedContributors { get; set; }

		/// <summary>
		/// Gets or sets the list of issues created by the current user.
		/// </summary>
		public IList<Issue> IssuesCreatedByCurrentUser { get; set; }

		/// <summary>
		/// Gets or sets the list of pull requests created by the current user.
		/// </summary>
		public IList<PullRequest> PullRequestsCreatedByCurrentUser { get; set; }

		public string AnalysisFolder => Path.Combine(this.TempFolder, "analysis");

		public string PackagesFolder => Path.Combine(this.TempFolder, "packages");

		public string ExcelReportPath => Path.Combine(this.TempFolder, "Audit.xlsx");

		public string MarkdownReportPath => Path.Combine(this.TempFolder, "Audit.md");

		public string StatsSaveLocation => Path.Combine(this.TempFolder, "Audit_stats.csv");

		public string GraphSaveLocation => Path.Combine(this.TempFolder, "Audit_progress.png");

		public string AnalysisResultSaveLocation => Path.Combine(this.TempFolder, "Analysis_result.json");
	}
}
