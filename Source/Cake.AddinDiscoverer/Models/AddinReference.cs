using Cake.AddinDiscoverer.Utilities;
using System.Diagnostics;

namespace Cake.AddinDiscoverer.Models
{
	[DebuggerDisplay("{Name} {ReferencedVersion}")]
	internal class AddinReference : CakeReference
	{
		public SemVersion LatestVersionForCurrentCake { get; set; }

		public SemVersion LatestVersionForNextCake { get; set; }

		public SemVersion LatestVersionForLatestCake { get; set; }

		public bool UpdatedForNextCake
		{
			get
			{
				// If both values are null it means that this addin does not reference Cake at all (e.g.: Cake.Email.Common)
				if (LatestVersionForCurrentCake == null && LatestVersionForNextCake == null) return true;

				// If this value is non-null it means that an updated version is available
				if (LatestVersionForNextCake != null) return true;

				// Otherwise, it hasn't been updated (or there is no new version of Cake that introduces breaking changes)
				return false;
			}
		}

		// Cake.Coverlet is an example of an Addin that supported Cake 1.0 and then Cake 3.0 but it skipped Cake 2.0
		public bool SkippedNextCake
		{
			get => LatestVersionForNextCake == null && LatestVersionForLatestCake != null;
		}
	}
}
