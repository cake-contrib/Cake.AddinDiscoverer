namespace Cake.AddinDiscoverer
{
	internal class AddinReference : CakeReference
	{
		public string LatestVersionForCurrentCake { get; set; }

		public string LatestVersionForLatestCake { get; set; }
	}
}
