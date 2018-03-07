
namespace Cake.AddinDiscoverer
{
	public class AddinAnalysisResult
	{
		public string CakeCoreVersion { get; set; }
		public string CakeCommonVersion { get; set; }
		public bool CakeCoreIsUpToDate { get; set; }
		public bool CakeCommonIsUpToDate { get; set; }
		public bool CakeCoreIsPrivate { get; set; }
		public bool CakeCommonIsPrivate { get; set; }
		public bool TargetsExpectedFramework { get; set; }
		public string Notes { get; set; }
	}
}
