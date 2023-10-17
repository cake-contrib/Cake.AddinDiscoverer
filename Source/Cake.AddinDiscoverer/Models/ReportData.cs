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
		public IEnumerable<AddinMetadata> GetAddinsForCakeVersion(CakeVersion cakeVersion, bool strict)
		{
			return AllPackages
				.GroupBy(addin => addin.Name, StringComparer.OrdinalIgnoreCase)
				.Select(group => MostRecentAddinVersion(group, cakeVersion, strict))
				.Where(addin => addin != null); // this condition filters out packages that were not publicly avaiable
		}

		// Returns the most recent version of an addin that is compatible with a given version of Cake
		private static AddinMetadata MostRecentAddinVersion(IEnumerable<AddinMetadata> addinVersions, CakeVersion cakeVersion, bool strict)
		{
			var cakeVersionZero = Constants.CAKE_VERSIONS.Single(cv => cv.Version == Constants.VERSION_ZERO);

			return addinVersions
					.Where(addinVersion =>
					{
						if (cakeVersion == null) return true;

						// Determine the targeted version of Cake by looking at the Cake.Core and/or Cake.Common references
						var targetedCakeVersion = addinVersion.AnalysisResult?.GetCakeVersion();

						// If the addin does not reference Care.Core or Cake.Common (most likely this means the addin is a recipe), fallback on the cake-version.yml
						targetedCakeVersion ??= Constants.CAKE_VERSIONS.SingleOrDefault(cv => cv.Version == addinVersion.CakeVersionYaml?.TargetCakeVersion);

						// If still unable to determine the targeted version of Cake (most likely this means the addin is a recipe lacking cake-version.yml), we assume this addin targets Cake pre-1.0.0
						targetedCakeVersion ??= cakeVersionZero;

						var comparisonResult = targetedCakeVersion.CompareTo(cakeVersion);
						return strict ? comparisonResult == 0 : comparisonResult <= 0;
					})
					.SortForAddinDisco()
					.FirstOrDefault();
		}
	}
}
