using Cake.Incubator.StringExtensions;
using Octokit;
using System;
using System.Diagnostics;

namespace Cake.AddinDiscoverer.Models
{
	[DebuggerDisplay("Name = {Name}; Type = {Type}")]
	internal class AddinMetadata
	{
		public string Name { get; set; }

		public string Maintainer { get; set; }

		public string RepositoryName { get; set; }

		public string RepositoryOwner { get; set; }

		public string[] Frameworks { get; set; }

		public DllReference[] References { get; set; }

		public AddinAnalysisResult AnalysisResult { get; set; }

		public Uri IconUrl { get; set; }

		public byte[] EmbeddedIcon { get; set; }

		public string NuGetPackageVersion { get; set; }

		public Uri NuGetPackageUrl { get; set; }

		public string[] NuGetPackageOwners { get; set; }

		public string License { get; set; }

		public Uri ProjectUrl { get; set; }

		public Uri RepositoryUrl { get; set; }

		public PullRequest AuditPullRequest { get; set; }

		public Issue AuditIssue { get; set; }

		public AddinType Type { get; set; }

		public bool IsDeprecated { get; set; }

		public string Description { get; set; }

		public string[] Tags { get; set; }

		public bool IsPrerelease { get; set; }

		public bool HasPrereleaseDependencies { get; set; }

		public string DllName { get; set; }

		public PdbStatus PdbStatus { get; set; }

		public string GetMaintainerName()
		{
			var maintainer = RepositoryOwner ?? Maintainer;
			if (maintainer.EqualsIgnoreCase("cake-contrib")) maintainer = Maintainer;
			return maintainer;
		}
	}
}
