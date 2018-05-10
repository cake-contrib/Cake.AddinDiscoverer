using System;

namespace Cake.AddinDiscoverer
{
	[Flags]
	internal enum DataDestination
	{
		None = 0,
		Markdown = 1 << 0,
		Excel = 1 << 1,
		All = Markdown | Excel
	}
}
