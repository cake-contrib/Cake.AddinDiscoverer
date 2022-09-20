using Cake.AddinDiscoverer.Utilities;

namespace Cake.AddinDiscoverer.Models
{
	internal class CakeVersionYamlConfig
	{
		public SemVersion TargetCakeVersion { get; set; }

		public string[] TargetFrameworks { get; set; }
	}
}
