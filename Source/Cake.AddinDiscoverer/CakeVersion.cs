using Cake.AddinDiscoverer.Utilities;
using System;

namespace Cake.AddinDiscoverer
{
	internal class CakeVersion : IComparable<CakeVersion>, IComparable
	{
		public SemVersion Version { get; set; }

		public string RequiredFramework { get; set; }

		public string OptionalFramework { get; set; }

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
