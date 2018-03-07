using CommandLine;

namespace Cake.AddinDiscoverer
{
	public class Options
	{
		[Option('c', Default = false, HelpText = "Clear the list of addins that was previously cached.")]
		public bool ClearCache { get; set; }

		[Option('i', Default = false, HelpText = "Create issue in Github repositories that do not meet recommendations.")]
		public bool CreateGithubIssues { get; set; }

		[Option('p', Required = false, HelpText = "Github password.")]
		public string GithuPassword { get; set; }

		[Option('t', Required = false, HelpText = "Folder where temporary files (including reports) are saved.")]
		public string TemporaryFolder { get; set; }

		[Option('u', Required = false, HelpText = "Github username.")]
		public string GithubUsername { get; set; }

		[Option('v', Required = true, HelpText = "The recommended Cake version. e.g.: 0.26.0")]
		public string RecommendedCakeVersion { get; set; }

		[Option("excel", Default = false, HelpText = "Generate the Excel report.")]
		public bool GenerateExcelReport { get; set; }

		[Option("markdown", Default = false, HelpText = "Generate the Markdown report.")]
		public bool GenerateMarkdownReport { get; set; }
	}
}
