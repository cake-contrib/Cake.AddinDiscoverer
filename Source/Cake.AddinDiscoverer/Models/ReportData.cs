using Cake.AddinDiscoverer.Utilities;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Cake.AddinDiscoverer.Models
{
	internal class ReportData
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
		public int? OpenIssuesCount { get; set; }

		/// <summary>
		/// Gets or sets the number of open pull requests in the github repository.
		/// </summary>
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
		/// Get the precise version of Cake targeted by this addin.
		/// </summary>
		/// <returns>The precise version of Cake targeted by this addin.</returns>
		public SemVersion GetTargetedCakeVersion()
		{
			if (CakeCoreVersion == null) return CakeCommonVersion;
			else if (CakeCommonVersion == null) return CakeCoreVersion;
			else return CakeCoreVersion >= CakeCommonVersion ? CakeCoreVersion : CakeCommonVersion;
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

		public ReportData GetAddinDataForReports()
		{
			var cakeVersionsForReport = Constants.CAKE_VERSIONS.Where(cakeVersion => cakeVersion.Version != Constants.VERSION_ZERO).ToArray();
			var allAddins = AddinsWithCakeVersionSupported(Addins);
			var distinctCount = allAddins.Length;

			var deprecated = allAddins.Where(grp => grp.MetadataForCakeVersion.Any(m => m.Metadata?.IsDeprecated ?? false)).ToArray();
			var auditedAddins = allAddins.Except(deprecated).ToArray();

			// Some addins have an older version that caused an excpetion during analysis but the problem was resolved in a more recent version
			// These addins should not be considered "exceptions".
			var exceptions = auditedAddins.Where(grp => !string.IsNullOrEmpty(grp.MetadataForCakeVersion.OrderByDescending(grp => grp.CakeVersion).First(grp => grp.Metadata is not null).Metadata.AnalysisResult?.Notes)).ToArray();
			auditedAddins = auditedAddins.Except(exceptions).ToArray();

			// Some addins have an older version that couldn't be categorized during analysis but the problem was resolved in a more recent version
			// These addins should not be considered "uncategorized".
			var uncategorized = auditedAddins.Where(grp => grp.MetadataForCakeVersion.OrderByDescending(grp => grp.CakeVersion).First(grp => grp.Metadata is not null).Metadata.Type == AddinType.Unknown).ToArray();
			auditedAddins = auditedAddins.Except(uncategorized).ToArray();

			var recipes = auditedAddins.Where(grp => grp.MetadataForCakeVersion.Any(m => m.Metadata?.Type == AddinType.Recipe)).ToArray();
			auditedAddins = auditedAddins.Except(recipes).ToArray();

			var modules = auditedAddins.Where(grp => grp.MetadataForCakeVersion.Any(m => m.Metadata?.Type == AddinType.Module)).ToArray();
			auditedAddins = auditedAddins.Except(modules).ToArray();

			var addins = auditedAddins.Where(grp => grp.MetadataForCakeVersion.Any(m => m.Metadata?.Type == AddinType.Addin)).ToArray();
			auditedAddins = auditedAddins.Except(addins).ToArray();

			Debug.Assert(!auditedAddins.Any(), "There shouldn't be any addins left in the list of audited addins");

			// For reporting purposes, we want addins and modules to be combined
			auditedAddins = addins.Union(modules).ToArray();

			return new ReportData()
			{
				DistinctCount = distinctCount,
				CakeVersionsForReport = cakeVersionsForReport,
				Deprecated = deprecated,
				Exceptions = exceptions,
				Uncategorized = uncategorized,
				Recipes = recipes,
				Modules = modules,
				AuditedAddins = auditedAddins,
			};
		}

		// Return all the addins with their most recent version supporting each version of Cake
		private static (string Name, (CakeVersion CakeVersion, AddinMetadata Metadata)[] MetadataForCakeVersion)[] AddinsWithCakeVersionSupported(IEnumerable<AddinMetadata> allAddins)
		{
			// Find the most recent version of each addin that is compatible with the given version of Cake
			return allAddins.GroupBy(metadata => metadata.Name)
				.Select(group => (
					group.Key,
					Constants.CAKE_VERSIONS
						.Select(cakeVersion => (CakeVersion: cakeVersion, Metadata: MostRecentAddinVersion(group, cakeVersion)))
						.ToArray()
				))
				.ToArray();
		}

		// Returns the most recent version of each addins that is compatible with a given version of Cake
		// When a given addin is not compatible with the desired version of Cake, the latest version of said addin is returned.
		private static AddinMetadata[] MostRecentAddinsVersion((string Name, (CakeVersion CakeVersion, AddinMetadata Metadata)[] MetadataForCakeVersion)[] addins, CakeVersion cakeVersion = null)
		{
			return addins
				.SelectMany(info => info.MetadataForCakeVersion)
				.Where(m => m.Metadata is not null)
				.GroupBy(m => m.Metadata.Name)
				.Select(x => x
					.OrderBy(m => cakeVersion == null ? 0 : m.CakeVersion == cakeVersion ? 1 : 2)
					.ThenByDescending(m => m.CakeVersion)
					.First()
					.Metadata)
				.ToArray();
		}

		// Return the most recent version of an addin that is compatible with a given version of Cake
		private static AddinMetadata MostRecentAddinVersion(IEnumerable<AddinMetadata> addinVersions, CakeVersion cakeVersion = null)
		{
			// Find the most recent version of an each addin that is compatible with the given version of Cake
			return addinVersions
					.Where(addinVersion => cakeVersion == null || (addinVersion.AnalysisResult?.GetCakeVersion() ?? Constants.CAKE_VERSIONS.Single(cv => cv.Version == Constants.VERSION_ZERO)) == cakeVersion)
					.OrderBy(addinVersion => addinVersion.IsPrerelease ? 1 : 0) // Stable versions are sorted first, prerelease versions sorted second)
					.ThenByDescending(addinVersion => addinVersion.PublishedOn)
					.FirstOrDefault();
		}

	}
}
