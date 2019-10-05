using System.Diagnostics;

namespace Cake.AddinDiscoverer.Models
{
	[DebuggerDisplay("{Name} {ReferencedVersion}")]
	internal class ToolReference : CakeReference
	{
		public string LatestVersion { get; set; }
	}
}
