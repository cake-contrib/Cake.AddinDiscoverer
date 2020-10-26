using Cake.AddinDiscoverer.Utilities;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Cake.AddinDiscoverer.Models
{
	/// <summary>
	/// Contains the result of the analysis of a given addin.
	/// </summary>
	internal class AddinAnalysisResult
	{
		/// <summary>
		/// Gets or sets the version of Cake.Core referenced by this addin or a null value if the addin does not reference Cake.Core.
		/// </summary>
		public SemVersion CakeCoreVersion { get; set; }

		/// <summary>
		/// Gets or sets the version of Cake.Common referenced by this addin or a null value if the addin does not reference Cake.Common.
		/// </summary>
		public SemVersion CakeCommonVersion { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether reference to Cake.Core is private.
		/// </summary>
		public bool CakeCoreIsPrivate { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether reference to Cake.Common is private.
		/// </summary>
		public bool CakeCommonIsPrivate { get; set; }

		/// <summary>
		/// Gets or sets the result of the icon analysis.
		/// </summary>
		public IconAnalysisResult Icon { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether the addin was transferred to the cake-contrib github organization.
		/// </summary>
		public bool TransferredToCakeContribOrganization { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether the cake-contrib user has been added as an owner of the NuGet package.
		/// </summary>
		public bool PackageCoOwnedByCakeContrib { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether the obsolete licenseUrl property has been removed from the nuspec.
		/// </summary>
		public bool ObsoleteLicenseUrlRemoved { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether the repository info (such as URL, type, etc.) is provided in the nuspec.
		/// </summary>
		public bool RepositoryInfoProvided { get; set; }

		/// <summary>
		/// Gets or sets notes (such as error messages).
		/// </summary>
		public string Notes { get; set; }

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

		/// <summary>
		/// Gets or sets a value indicating whether a Cake.Recipe is used to build this addin.
		/// </summary>
		public bool CakeRecipeIsUsed { get; set; }

		/// <summary>
		/// Gets or sets the version of Cake.Recipe used to build this addin. A null value indicates that Cake.Recipe is not used.
		/// </summary>
		public SemVersion CakeRecipeVersion { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether a prerelease version of Cake.Recipe is used to build this addin.
		/// </summary>
		public bool CakeRecipeIsPrerelease { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether the latest version of Cake.Recipe is used to build this addin.
		/// </summary>
		public bool CakeRecipeIsLatest { get; set; }

		public bool AtLeastOneDecoratedMethod { get; set; }

		/// <summary>
		/// Gets or sets a value indicating whether XML documentation is available in the NuGet package.
		/// </summary>
		public bool XmlDocumentationAvailable { get; set; }

		public List<string> XmlDocumentationAnalysisNotes { get; set; } = new List<string>();

		/// <summary>
		/// Get the precise version of Cake targeted by this addin.
		/// </summary>
		/// <returns>The precise version of Cake targeted by this addin.</returns>
		public SemVersion GetTargetedCakeVersion()
		{
			if (CakeCoreVersion == null) return CakeCommonVersion;
			else if (CakeCommonVersion == null) return CakeCoreVersion;
			else return CakeCoreVersion < CakeCommonVersion ? CakeCoreVersion : CakeCommonVersion; // Return the lowest version. This only matters when an addin targets different versions, which would be extremely weird!
		}

		/// <summary>
		/// Get the version of Cake targeted by this addin.
		/// </summary>
		/// <returns>The version of Cake targeted by this addin.</returns>
		public CakeVersion GetCakeVersion()
		{
			var targetedVersion = GetTargetedCakeVersion();
			if (targetedVersion == null) return null;

			return Constants.CAKE_VERSIONS
				.Where(v => v.Version.Major <= targetedVersion.Major)
				.OrderByDescending(v => v.Version)
				.First();
		}
	}
}
