using System;

namespace Cake.AddinDiscoverer
{
	internal static class Extensions
	{
		public static string WithRightPadding(this string content, int desiredLength)
		{
			var count = Math.Max(0, desiredLength - content.Length);
			return content + new string(' ', count);
		}
	}
}
