using System;

namespace Cake.AddinDiscoverer
{
	public class AddinMetadata
	{
		private Uri repositoryUrl;

		public string Name { get; set; }
		public string GithubRepoName { get; private set; }
		public string GithubRepoOwner { get; private set; }
		public string SolutionPath { get; set; }
		public string[] ProjectPaths { get; set; }
		public string[] Frameworks { get; set; }
		public (string Id, string Version, bool IsPrivate)[] References { get; set; }
		public AddinAnalysisResult AnalysisResult { get; set; }
		public AddinMetadataSource Source { get; set; }
		public Uri IconUrl { get; set; }
		public Uri RepositoryUrl
		{
			get
			{
				return repositoryUrl;
			}
			set
			{
				repositoryUrl = value;

				if (value != null && value.Host.Contains("github"))
				{
					var parts = value.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
					if (parts.Length >= 2)
					{
						this.GithubRepoOwner = parts[0];
						this.GithubRepoName = parts[1];
					}
				}
			}
		}
		public Uri GithubIssueUrl { get; set; }
		public int? GithubIssueId { get; set; }

		public bool IsValid()
		{
			return this.RepositoryUrl != null &&
				this.RepositoryUrl.Host.Contains("github") &&
				!string.IsNullOrEmpty(this.GithubRepoName) &&
				!string.IsNullOrEmpty(this.GithubRepoOwner);
		}
	}
}
