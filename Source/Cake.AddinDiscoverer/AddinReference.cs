namespace Cake.AddinDiscoverer
{
	internal class AddinReference
	{
		public string Name { get; set; }

		public string ReferencedVersion { get; set; }

		public string LatestVersionForCurrentCake { get; set; }

		public string LatestVersionForLatestCake { get; set; }
	}
}
