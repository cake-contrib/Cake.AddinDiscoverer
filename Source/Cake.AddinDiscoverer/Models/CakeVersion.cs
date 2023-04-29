using Cake.AddinDiscoverer.Utilities;
using System;
using System.Diagnostics;

namespace Cake.AddinDiscoverer.Models
{
	[DebuggerDisplay("Cake {Version.ToString()}")]
	internal class CakeVersion : IComparable<CakeVersion>, IComparable
	{
		public SemVersion Version { get; set; }

		public string[] RequiredFrameworks { get; set; }

		public string[] OptionalFrameworks { get; set; }

		public int CompareTo(object obj)
		{
			return CompareTo((CakeVersion)obj);
		}

		public int CompareTo(CakeVersion other)
		{
			return this.Version.CompareTo(other.Version);
		}
	}
}
