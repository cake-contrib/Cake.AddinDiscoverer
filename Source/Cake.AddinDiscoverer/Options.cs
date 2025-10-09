using CommandLine;

namespace Cake.AddinDiscoverer
{
	internal class Options
	{
		[Option('a', "addinname", Required = false, HelpText = "Name of the specific addin to be audited. If omitted, all addins are audited.")]
		public string AddinName { get; set; }

		[Option('c', "clearcache", Default = false, HelpText = "Clear the list of addins that was previously cached.")]
		public bool ClearCache { get; set; }

		[Option('d', "dryrun", Default = false, HelpText = "Dry run. Do not create PRs.")]
		public bool DryRun { get; set; }

		[Option('e', "generateexcel", Default = false, HelpText = "Generate the Excel report and write to a file.")]
		public bool GenerateExcelReport { get; set; }

		[Option('i', "issue", Default = false, HelpText = "Create issue in Github repositories that do not meet recommendations.")]
		public bool CreateGithubIssue { get; set; }

		[Option('k', "updaterecipes", Default = false, HelpText = "Update addin references in recipes such as Cake.Recipe and Chocolatey.Cake.Recipe.")]
		public bool UpdateRecipes { get; set; }

		[Option('l', "uselocalresources", Default = false, HelpText = "Use local resource files instead of downloading them from GitHub.")]
		public bool UseLocalResources { get; set; }

		[Option('m', "generatemarkdown", Default = false, HelpText = "Generate the Markdown report and write to a file.")]
		public bool GenerateMarkdownReport { get; set; }

		[Option('n', "contributors", Default = false, HelpText = "Synchronize the list of contributors.")]
		public bool SynchronizeContributors { get; set; }

		[Option('o', "token", Required = false, HelpText = "Github token (takes precedence over username+password).")]
		public string GithubToken { get; set; }

		[Option('p', "password", Required = false, HelpText = "Github password.")]
		public string GithuPassword { get; set; }

		[Option('q', "pullrequest", Default = false, HelpText = "Submit pull request in Github repositories to fix recommendations.")]
		public bool SubmitGithubPullRequest { get; set; }

		[Option('r', "committorepo", Default = false, HelpText = "Commit reports and other files to cake-contrib repo.")]
		public bool CommitToRepo { get; set; }

		[Option('s', "syncyaml", Default = false, HelpText = "Synchronize the yaml files on Cake's web site with the packages discovered on NuGet.")]
		public bool SynchronizeYaml { get; set; }

		[Option('t', "tempfolder", Required = false, HelpText = "Folder where temporary files (including reports) are saved.")]
		public string TemporaryFolder { get; set; }

		[Option('u', "user", Required = false, HelpText = "Github username.")]
		public string GithubUsername { get; set; }

		[Option('w', "excludeslowsteps", Default = false, HelpText = "Exclude steps that take a lot of time to complete (such as 'Get stats from Github').")]
		public bool ExcludeSlowSteps { get; set; }

		[Option('y', "proxy", Required = false, HelpText = "The URL of your proxy. For example, to proxy requests through Fiddler use: 'http://localhost:8888'.")]
		public string ProxyUrl { get; set; }

		[Option('z', "analyzealladdins", Default = false, HelpText = "Analyze all addins as opposed to only the new ones.")]
		public bool AnalyzeAllAddins { get; set; }
	}
}
