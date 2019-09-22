using System;

namespace Cake.AddinDiscoverer
{
	[Flags]
	internal enum AddinType
	{
		Unknown = 0,
		Addin = 1 << 0,
		Recipe = 1 << 1,
		Module = 1 << 2,
		All = Addin | Recipe | Module
	}
}
