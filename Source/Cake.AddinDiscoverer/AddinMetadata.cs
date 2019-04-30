using Cake.Incubator.StringExtensions;
using System;
using System.Diagnostics;

namespace Cake.AddinDiscoverer
{
	[DebuggerDisplay("Name = {Name}; Type = {Type}")]
	internal class AddinMetadata
	{
		private Uri repositoryUrl;

		public string Name { get; set; }

		public string Maintainer { get; set; }

		public string GithubRepoName { get; private set; }

		public string GithubRepoOwner { get; private set; }

		public string[] Frameworks { get; set; }

		public DllReference[] References { get; set; }

		public AddinAnalysisResult AnalysisResult { get; set; }

		public Uri IconUrl { get; set; }

		public string NuGetPackageVersion { get; set; }

		public Uri NuGetPackageUrl { get; set; }

		public string NuGetLicense { get; set; }

		public Uri GithubRepoUrl
		{
			get
			{
				return repositoryUrl;
			}

			set
			{
				repositoryUrl = value;

				if (value != null)
				{
					var parts = value.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
					if (parts.Length >= 2)
					{
						this.GithubRepoOwner = parts[0];
						this.GithubRepoName = parts[1].TrimEnd(".git", StringComparison.OrdinalIgnoreCase);
					}
				}
			}
		}

		public int? GithubPullRequestId { get; set; }

		public int? GithubIssueId { get; set; }

		public AddinType Type { get; set; }

		public bool IsDeprecated { get; set; }

		public string Description { get; set; }

		public string[] Tags { get; set; }

		public bool IsPrerelease { get; set; }

		public string DllName { get; set; }

		public string GetMaintainerName()
		{
			var maintainer = GithubRepoOwner ?? Maintainer;
			if (maintainer.EqualsIgnoreCase("cake-contrib")) maintainer = Maintainer;
			return maintainer;
		}
	}
}
