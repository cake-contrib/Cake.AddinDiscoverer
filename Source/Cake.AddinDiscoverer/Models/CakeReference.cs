using Cake.AddinDiscoverer.Utilities;
using System.Diagnostics;

namespace Cake.AddinDiscoverer.Models
{
	[DebuggerDisplay("{Name} {ReferencedVersion}")]
	internal class CakeReference
	{
		public string Name { get; set; }

		public SemVersion ReferencedVersion { get; set; }

		public bool Prerelease { get; set; }
	}
}
