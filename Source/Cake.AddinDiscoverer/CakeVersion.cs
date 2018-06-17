using Cake.AddinDiscoverer.Utilities;

namespace Cake.AddinDiscoverer
{
	internal class CakeVersion
	{
		public SemVersion Version { get; set; }

		public string Framework { get; set; }
	}
}
