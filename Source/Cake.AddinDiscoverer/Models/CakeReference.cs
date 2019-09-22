namespace Cake.AddinDiscoverer.Models
{
	internal class CakeReference
	{
		public string Name { get; set; }

		public string ReferencedVersion { get; set; }

		public bool Prerelease { get; set; }
	}
}
