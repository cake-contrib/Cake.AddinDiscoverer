using CommandLine;

namespace Cake.AddinDiscoverer
{
	internal class Options
	{
		[Option('a', "addinname", Required = false, HelpText = "Name of the specific addin to be audited. If omitted, all addins are audited.")]
		public string AddinName { get; set; }

		[Option('c', "clearcache", Default = false, HelpText = "Clear the list of addins that was previously cached.")]
		public bool ClearCache { get; set; }

		[Option('u', "user", Required = false, HelpText = "Github username.")]
		public string GithubUsername { get; set; }

		[Option('p', "password", Required = false, HelpText = "Github password.")]
		public string GithuPassword { get; set; }

		[Option('i', "issue", Default = false, HelpText = "Create issue in Github repositories that do not meet recommendations.")]
		public bool CreateGithubIssue { get; set; }

		[Option('e', "excel", Default = false, HelpText = "Generate the Excel report.")]
		public bool GenerateExcelReport { get; set; }

		[Option('m', "markdown", Default = false, HelpText = "Generate the Markdown report and write in a file.")]
		public bool MarkdownReportToFile { get; set; }

		[Option('r', "markdown", Default = false, HelpText = "Generate the Markdown report and commit to cake-contrib repo.")]
		public bool MarkdownReportToRepo { get; set; }

		[Option('t', "tempfolder", Required = false, HelpText = "Folder where temporary files (including reports) are saved.")]
		public string TemporaryFolder { get; set; }

		[Option('v', "cakeversion", Required = true, HelpText = "The recommended Cake version. e.g.: 0.26.0")]
		public string RecommendedCakeVersion { get; set; }
	}
}
