using System.Diagnostics;

namespace Cake.AddinDiscoverer.Models
{
	[DebuggerDisplay("{Name} {ReferencedVersion}")]
	internal class AddinReference : CakeReference
	{
		public string LatestVersionForCurrentCake { get; set; }

		public string LatestVersionForLatestCake { get; set; }
	}
}
