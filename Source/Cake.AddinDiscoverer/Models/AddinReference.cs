using Cake.AddinDiscoverer.Utilities;
using System.Diagnostics;

namespace Cake.AddinDiscoverer.Models
{
	[DebuggerDisplay("{Name} {ReferencedVersion}")]
	internal class AddinReference : CakeReference
	{
		public SemVersion LatestVersionForAnyPreviousCake { get; set; }

		public SemVersion LatestVersionForCurrentCake { get; set; }

		public SemVersion LatestVersionForNextCake { get; set; }

		public SemVersion LatestVersionForLatestCake { get; set; }

		public bool UpdatedForNextCake
		{
			get
			{
				// If all values are null it means that this addin does not reference Cake at all (e.g.: Cake.Email.Common)
				// We make the assumption that this addin does not need to be updated to be compatible with next Cake.
				// Hence why we return 'true' which indicates that we consider this addin to be up-to-date.
				if (LatestVersionForAnyPreviousCake == null && LatestVersionForCurrentCake == null && LatestVersionForNextCake == null) return true;

				// If the addin is not compatible with current Cake nor with all subsequent version of Cake, it means that
				// this addin was up-to-date at some point in the past and has not been updated in some time.
				// We return false to indicate that the addin is not up-to-date.
				else if (LatestVersionForCurrentCake == null && LatestVersionForNextCake == null) return false;

				// If this value is non-null it means that an updated version is available
				else if (LatestVersionForNextCake != null) return true;

				// Otherwise, it hasn't been updated (or there is no new version of Cake that introduces breaking changes)
				else return false;
			}
		}

		// Cake.Coverlet is an example of an Addin that supported Cake 1.0 and then Cake 3.0 but it skipped Cake 2.0
		public bool SkippedNextCake
		{
			get => LatestVersionForNextCake == null && LatestVersionForLatestCake != null;
		}
	}
}
