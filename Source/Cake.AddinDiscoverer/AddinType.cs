using System;

namespace Cake.AddinDiscoverer
{
	[Flags]
	internal enum AddinType
	{
		Unknown = 0,
		Addin = 1 << 0,
		Recipes = 1 << 1,
		All = Addin | Recipes
	}
}
