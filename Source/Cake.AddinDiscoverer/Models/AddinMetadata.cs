using Cake.Incubator.StringExtensions;
using Octokit;
using System;
using System.Diagnostics;
using System.Reflection;

namespace Cake.AddinDiscoverer.Models
{
	[DebuggerDisplay("Name = {Name}; Type = {Type}")]
	internal class AddinMetadata
	{
		public string Name { get; set; }

		public string Maintainer { get; set; }

		public string RepositoryName { get; set; }

		public string RepositoryOwner { get; set; }

		public string[] Frameworks { get; set; } = Array.Empty<string>();

		public DllReference[] References { get; set; } = Array.Empty<DllReference>();

		public AddinAnalysisResult AnalysisResult { get; set; }

		public Uri IconUrl { get; set; }

		public byte[] EmbeddedIcon { get; set; } = Array.Empty<byte>();

		public string NuGetPackageVersion { get; set; }

		public Uri NuGetPackageUrl { get; set; }

		public string[] NuGetPackageOwners { get; set; } = Array.Empty<string>();

		public string License { get; set; }

		public Uri ProjectUrl { get; set; }

		public Uri RepositoryUrl { get; set; }

		public PullRequest AuditPullRequest { get; set; }

		public Issue AuditIssue { get; set; }

		public AddinType Type { get; set; }

		public bool IsDeprecated { get; set; }

		public string Description { get; set; }

		public string[] Tags { get; set; } = Array.Empty<string>();

		public bool IsPrerelease { get; set; }

		public bool HasPrereleaseDependencies { get; set; }

		public string DllName { get; set; }

		public PdbStatus PdbStatus { get; set; }

		public bool SourceLinkEnabled { get; set; }

		public MethodInfo[] DecoratedMethods { get; set; } = Array.Empty<MethodInfo>();

		public string GetMaintainerName()
		{
			var maintainer = RepositoryOwner ?? Maintainer;
			if (maintainer.EqualsIgnoreCase("cake-contrib")) maintainer = Maintainer;
			return maintainer;
		}
	}
}
