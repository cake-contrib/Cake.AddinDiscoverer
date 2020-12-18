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

		/// <summary>
		/// Gets or sets the URL provided by the addin author in the package nuspec.
		/// </summary>
		/// <remarks>
		/// Can be null if author omitted this information.
		/// </remarks>
		public Uri RepositoryUrl { get; set; }

		/// <summary>
		/// Gets or sets the URL inferred by the AddinDiscoverer based on the project URL.
		/// </summary>
		/// <remarks>
		/// Currently, we can only infer the repo URL if the project is hosted on GitHub.
		/// </remarks>
		public Uri InferredRepositoryUrl { get; set; }

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

		public bool SourceLinkEnabled { get; set; }

		public bool XmlDocumentationAvailable { get; set; }

		public MethodInfo[] DecoratedMethods { get; set; }

		public string[] AliasCategories { get; set; }

		public DateTimeOffset PublishedOn { get; set; }

		public string GetMaintainerName()
		{
			var maintainer = RepositoryOwner ?? Maintainer;
			if (maintainer.EqualsIgnoreCase("cake-contrib")) maintainer = Maintainer;
			return maintainer;
		}
	}
}
