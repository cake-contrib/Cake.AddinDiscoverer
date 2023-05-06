using Cake.AddinDiscoverer.Utilities;
using System.Diagnostics;

namespace Cake.AddinDiscoverer.Models
{
	[DebuggerDisplay("{Name} {ReferencedVersion}")]
	internal class ToolReference : CakeReference
	{
		public SemVersion LatestVersion { get; set; }
	}
}
