using CommandLine;

namespace Cake.AddinDiscoverer
{
	internal class Options
	{
		[Option('t', "tempfolder", Required = false, HelpText = "Folder where temporary files (including reports) are saved.")]
		public string TemporaryFolder { get; set; }

		[Option('a', "addinname", Required = false, HelpText = "Name of the specific addin to be audited. If omitted, all addins are audited.")]
		public string AddinName { get; set; }

		[Option('u', "user", Required = false, HelpText = "Github username.")]
		public string GithubUsername { get; set; }

		[Option('p', "password", Required = false, HelpText = "Github password.")]
		public string GithuPassword { get; set; }

		[Option('c', "clearcache", Default = false, HelpText = "Clear the list of addins that was previously cached.")]
		public bool ClearCache { get; set; }

		[Option('i', "issue", Default = false, HelpText = "Create issue in Github repositories that do not meet recommendations.")]
		public bool CreateGithubIssue { get; set; }

		[Option('e', "exceltofile", Default = false, HelpText = "Generate the Excel report and write to a file.")]
		public bool ExcelReportToFile { get; set; }

		[Option('x', "exceltorepo", Default = false, HelpText = "Generate the Excel report and commit to cake-contrib repo.")]
		public bool ExcelReportToRepo { get; set; }

		[Option('m', "markdowntofile", Default = false, HelpText = "Generate the Markdown report and write to a file.")]
		public bool MarkdownReportToFile { get; set; }

		[Option('r', "markdowntorepo", Default = false, HelpText = "Generate the Markdown report and commit to cake-contrib repo.")]
		public bool MarkdownReportToRepo { get; set; }

		[Option('s', "syncyaml", Default = false, HelpText = "Synchronize the yaml files on Cake's web site with the packages discovered on Nuget.")]
		public bool SynchronizeYaml { get; set; }
	}
}
