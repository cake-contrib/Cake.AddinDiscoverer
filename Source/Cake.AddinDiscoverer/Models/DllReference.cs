using Cake.AddinDiscoverer.Utilities;

namespace Cake.AddinDiscoverer
{
	internal class DllReference
	{
		public string Id { get; set; }

		public SemVersion Version { get; set; }

		public bool IsPrivate { get; set; }
	}
}
