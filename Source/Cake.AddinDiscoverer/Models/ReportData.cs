using Cake.AddinDiscoverer.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cake.AddinDiscoverer.Models
{
	internal class ReportData
	{
		public IEnumerable<AddinMetadata> AllPackages { get; private set; }

		public ReportData(IEnumerable<AddinMetadata> packages)
		{
			AllPackages = packages;
		}

		/// <summary>
		/// Returns distinct packages that were publicly available for download
		/// at the time the specified Cake version was the most recent version.
		/// </summary>
		/// <param name="cakeVersion">The desired Cake version.</param>
		/// <returns>An enumeration of Addins.<returns>
		public IEnumerable<AddinMetadata> GetAddinsForCakeVersion(CakeVersion cakeVersion)
		{
			return AllPackages
				.GroupBy(addin => addin.Name, StringComparer.OrdinalIgnoreCase)
				.Select(group => MostRecentAddinVersion(group, cakeVersion))
				.Where(addin => addin != null); // this condition filters out packages that were not publicly avaiable
		}

		// Returns the most recent version of an addin that is compatible with a given version of Cake
		private static AddinMetadata MostRecentAddinVersion(IEnumerable<AddinMetadata> addinVersions, CakeVersion cakeVersion = null)
		{
			var cakeVersionZero = Constants.CAKE_VERSIONS.Single(cv => cv.Version == Constants.VERSION_ZERO);

			return addinVersions
					.Where(addinVersion =>
					{
						if (cakeVersion == null) return true;

						var targetedCakeVersion = addinVersion.AnalysisResult?.GetCakeVersion() ?? cakeVersionZero;
						var comparisonResult = targetedCakeVersion.CompareTo(cakeVersion);

						return comparisonResult <= 0;
					})
					.OrderBy(addinVersion => addinVersion.IsPrerelease ? 1 : 0) // Stable versions are sorted first, prerelease versions sorted second)
					.ThenByDescending(addinVersion => addinVersion.PublishedOn)
					.FirstOrDefault();
		}
	}
}
