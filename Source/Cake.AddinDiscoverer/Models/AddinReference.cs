using System.Diagnostics;

namespace Cake.AddinDiscoverer.Models
{
	[DebuggerDisplay("{Name} {ReferencedVersion}")]
	internal class AddinReference : CakeReference
	{
		public string LatestVersionForCurrentCake { get; set; }

		public string LatestVersionForNextCake { get; set; }

		public bool UpdatedForNextCake
		{
			get
			{
				// If both values are null it means that this addin does not reference Cake at all (e.g.: Cake.Email.Common)
				if (string.IsNullOrEmpty(this.LatestVersionForCurrentCake) && string.IsNullOrEmpty(this.LatestVersionForNextCake)) return true;

				// If this value is non-null it means that an updated version is available
				if (!string.IsNullOrEmpty(this.LatestVersionForNextCake)) return true;

				// Otherwise, it hasn't been updated (or there is no new version of Cake that introduces breaking changes)
				return false;
			}
		}
	}
}
