using System;

namespace Cake.AddinDiscoverer.Models
{
	[Flags]
	internal enum DataDestination
	{
		None = 0,
		Excel = 1 << 0,
		MarkdownForAddins = 1 << 1,
		MarkdownForRecipes = 1 << 2,
		All = Excel | MarkdownForAddins | MarkdownForRecipes
	}
}
