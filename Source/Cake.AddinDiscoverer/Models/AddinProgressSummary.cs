using System;

namespace Cake.AddinDiscoverer
{
	internal class AddinProgressSummary
	{
		public string CakeVersion { get; set; }

		public DateTime Date { get; set; }

		public int CompatibleCount { get; set; }

		public int TotalCount { get; set; }
	}
}
