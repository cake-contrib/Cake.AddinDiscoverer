using Octokit;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Cake.AddinDiscoverer.Models
{
	[DebuggerDisplay("Name = {Name}")]
	internal class AddinMetadata
	{
		public string Name { get; set; }

		[JsonIgnore]
		public PullRequest AuditPullRequest { get; set; }

		[JsonIgnore]
		public Issue AuditIssue { get; set; }

		/// <summary>
		/// Gets or sets the number of open issues in the github repository.
		/// </summary>
		[JsonIgnore]
		public int? OpenIssuesCount { get; set; }

		/// <summary>
		/// Gets or sets the number of open pull requests in the github repository.
		/// </summary>
		[JsonIgnore]
		public int? OpenPullRequestsCount { get; set; }
	}
}
