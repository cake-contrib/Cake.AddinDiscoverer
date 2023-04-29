using Cake.AddinDiscoverer.Utilities;
using System.Collections.Generic;
using System.Linq;

namespace Cake.AddinDiscoverer.Models
{
	internal class ReportData
	{
		public IEnumerable<AddinMetadata> AllAddins { get; private set; }

		public IEnumerable<AddinMetadata> DeprecatedAddins => AllAddins.Where(addin => addin.IsDeprecated);

		public IEnumerable<AddinMetadata> AuditedAddins => AllAddins.Where(addin => !addin.IsDeprecated && string.IsNullOrEmpty(addin.AnalysisResult.Notes));

		public IEnumerable<AddinMetadata> ExceptionsAddins => AllAddins.Where(addin => !addin.IsDeprecated && !string.IsNullOrEmpty(addin.AnalysisResult.Notes));

		public ReportData(IEnumerable<AddinMetadata> addins)
		{
			AllAddins = addins;
		}

		/// <summary>
		/// Returns audited Addins that were publicly available for download
		/// at the time the specified Cake version was the most recent version.
		/// </summary>
		/// <param name="cakeVersion">The desired Cake version.</param>
		/// <returns>An enumeration of Addins.<returns>
		public IEnumerable<AddinMetadata> GetAuditedAddinsForCakeVersion(CakeVersion cakeVersion)
		{
			return AuditedAddins
				.GroupBy(addin => addin.Name)
				.Select(group => MostRecentAddinVersion(group, cakeVersion))
				.Where(addin => addin != null); // this condition filters out addins that were not publicly avaiable
		}

		//public ReportData GetAddinDataForReports()
		//{
		//	var cakeVersionsForReport = Constants.CAKE_VERSIONS.Where(cakeVersion => cakeVersion.Version != Constants.VERSION_ZERO).ToArray();
		//	var allAddins = AddinsWithCakeVersionSupported(Addins);
		//	var distinctCount = allAddins.Length;

		//	var deprecated = allAddins.Where(grp => grp.MetadataForCakeVersion.Any(m => m.Metadata?.IsDeprecated ?? false)).ToArray();
		//	var auditedAddins = allAddins.Except(deprecated).ToArray();

		//	// Some addins have an older version that caused an excpetion during analysis but the problem was resolved in a more recent version
		//	// These addins should not be considered "exceptions".
		//	var exceptions = auditedAddins.Where(grp => !string.IsNullOrEmpty(grp.MetadataForCakeVersion.OrderByDescending(grp => grp.CakeVersion).First(grp => grp.Metadata is not null).Metadata.AnalysisResult?.Notes)).ToArray();
		//	auditedAddins = auditedAddins.Except(exceptions).ToArray();

		//	// Some addins have an older version that couldn't be categorized during analysis but the problem was resolved in a more recent version
		//	// These addins should not be considered "uncategorized".
		//	var uncategorized = auditedAddins.Where(grp => grp.MetadataForCakeVersion.OrderByDescending(grp => grp.CakeVersion).First(grp => grp.Metadata is not null).Metadata.Type == AddinType.Unknown).ToArray();
		//	auditedAddins = auditedAddins.Except(uncategorized).ToArray();

		//	var recipes = auditedAddins.Where(grp => grp.MetadataForCakeVersion.Any(m => m.Metadata?.Type == AddinType.Recipe)).ToArray();
		//	auditedAddins = auditedAddins.Except(recipes).ToArray();

		//	var modules = auditedAddins.Where(grp => grp.MetadataForCakeVersion.Any(m => m.Metadata?.Type == AddinType.Module)).ToArray();
		//	auditedAddins = auditedAddins.Except(modules).ToArray();

		//	var addins = auditedAddins.Where(grp => grp.MetadataForCakeVersion.Any(m => m.Metadata?.Type == AddinType.Addin)).ToArray();
		//	auditedAddins = auditedAddins.Except(addins).ToArray();

		//	Debug.Assert(!auditedAddins.Any(), "There shouldn't be any addins left in the list of audited addins");

		//	// For reporting purposes, we want addins and modules to be combined
		//	auditedAddins = addins.Union(modules).ToArray();

		//	return new ReportData()
		//	{
		//		DistinctCount = distinctCount,
		//		CakeVersionsForReport = cakeVersionsForReport,
		//		Deprecated = deprecated,
		//		Exceptions = exceptions,
		//		Uncategorized = uncategorized,
		//		Recipes = recipes,
		//		Modules = modules,
		//		AuditedAddins = auditedAddins,
		//	};
		//}

		// Returns a matrix of addins and which of their release supports the various versions of Cake
		private static (string Name, (CakeVersion CakeVersion, AddinMetadata Metadata)[] MetadataForCakeVersion)[] AddinsWithCakeVersionSupported(IEnumerable<AddinMetadata> allAddins)
		{
			return allAddins.GroupBy(metadata => metadata.Name)
				.Select(group => (
					group.Key,
					Constants.CAKE_VERSIONS
						.Select(cakeVersion => (CakeVersion: cakeVersion, Metadata: MostRecentAddinVersion(group, cakeVersion)))
						.ToArray()))
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

		// Returns the most recent version of an addin that is compatible with a given version of Cake
		private static AddinMetadata MostRecentAddinVersion(IEnumerable<AddinMetadata> addinVersions, CakeVersion cakeVersion = null)
		{
			var cakeVersionZero = Constants.CAKE_VERSIONS.Single(cv => cv.Version == Constants.VERSION_ZERO);

			return addinVersions
					.Where(addinVersion => cakeVersion == null || (addinVersion.AnalysisResult?.GetCakeVersion() ?? cakeVersionZero).CompareTo(cakeVersion) <= 0)
					.OrderBy(addinVersion => addinVersion.IsPrerelease ? 1 : 0) // Stable versions are sorted first, prerelease versions sorted second)
					.ThenByDescending(addinVersion => addinVersion.PublishedOn)
					.FirstOrDefault();
		}
	}
}
