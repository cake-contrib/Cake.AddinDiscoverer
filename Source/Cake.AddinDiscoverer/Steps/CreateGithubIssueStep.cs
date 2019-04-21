using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
using Octokit;
using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class CreateGithubIssueStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => context.Options.CreateGithubIssue;

		public string GetDescription(DiscoveryContext context) => "Create Github issues";

		public async Task ExecuteAsync(DiscoveryContext context)
		{
			var recommendedCakeVersion = Constants.CAKE_VERSIONS
				.OrderByDescending(cakeVersion => cakeVersion.Version)
				.First();

			context.Addins = await context.Addins
				.ForEachAsync(
					async addin =>
					{
						if (addin.Type != AddinType.Recipe &&
							!addin.GithubIssueId.HasValue &&
							!string.IsNullOrEmpty(addin.GithubRepoName) &&
							!string.IsNullOrEmpty(addin.GithubRepoOwner))
						{
							var issuesDescription = new StringBuilder();
							if (addin.AnalysisResult.CakeCoreVersion == Constants.UNKNOWN_VERSION)
							{
								issuesDescription.AppendLine($"- [ ] We were unable to determine what version of Cake.Core your addin is referencing. Please make sure you are referencing {recommendedCakeVersion.Version}");
							}
							else if (!addin.AnalysisResult.CakeCoreVersion.IsUpToDate(recommendedCakeVersion.Version))
							{
								issuesDescription.AppendLine($"- [ ] You are currently referencing Cake.Core {addin.AnalysisResult.CakeCoreVersion.ToString()}. Please upgrade to {recommendedCakeVersion.Version.ToString()}");
							}

							if (addin.AnalysisResult.CakeCommonVersion == Constants.UNKNOWN_VERSION)
							{
								issuesDescription.AppendLine($"- [ ] We were unable to determine what version of Cake.Common your addin is referencing. Please make sure you are referencing {recommendedCakeVersion.Version}");
							}
							else if (!addin.AnalysisResult.CakeCommonVersion.IsUpToDate(recommendedCakeVersion.Version))
							{
								issuesDescription.AppendLine($"- [ ] You are currently referencing Cake.Common {addin.AnalysisResult.CakeCommonVersion.ToString()}. Please upgrade to {recommendedCakeVersion.Version.ToString()}");
							}

							if (!addin.AnalysisResult.CakeCoreIsPrivate) issuesDescription.AppendLine($"- [ ] The Cake.Core reference should be private. Specifically, your addin's `.csproj` should have a line similar to this: `<PackageReference Include=\"Cake.Core\" Version=\"{recommendedCakeVersion.Version}\" PrivateAssets=\"All\" />`");
							if (!addin.AnalysisResult.CakeCommonIsPrivate) issuesDescription.AppendLine($"- [ ] The Cake.Common reference should be private. Specifically, your addin's `.csproj` should have a line similar to this: `<PackageReference Include=\"Cake.Common\" Version=\"{recommendedCakeVersion.Version}\" PrivateAssets=\"All\" />`");
							if (!Misc.IsFrameworkUpToDate(addin.Frameworks, recommendedCakeVersion)) issuesDescription.AppendLine($"- [ ] Your addin should target {recommendedCakeVersion.RequiredFramework} at a minimum. Optionally, your addin can also multi-target {recommendedCakeVersion.OptionalFramework}.");

							if (!addin.AnalysisResult.UsingNewCakeContribIcon)
							{
								issuesDescription.AppendLine($"- [ ] The nuget package for your addin should use the cake-contrib icon. Specifically, your addin's `.csproj` should have a line like this: `<PackageIconUrl>{Constants.NEW_CAKE_CONTRIB_ICON_URL}</PackageIconUrl>`.");
							}

							if (issuesDescription.Length > 0)
							{
								// Create a new issue
								var issueBody = $"We performed an automated audit of your Cake addin and found that it does not follow all the best practices.{Environment.NewLine}{Environment.NewLine}";
								issueBody += $"We encourage you to make the following modifications:{Environment.NewLine}{Environment.NewLine}";
								issueBody += issuesDescription.ToString();
								issueBody += $"{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}";
								issueBody += $"Apologies if this is already being worked on, or if there are existing open issues, this issue was created based on what is currently published for this package on NuGet.{Environment.NewLine}";
								issueBody += $"{Environment.NewLine}This issue was created by a tool: Cake.AddinDiscoverer version {context.Version}{Environment.NewLine}";

								var newIssue = new NewIssue(Constants.ISSUE_TITLE)
								{
									Body = issueBody.ToString()
								};

								try
								{
									var issue = await context.GithubClient.Issue.Create(addin.GithubRepoOwner, addin.GithubRepoName, newIssue).ConfigureAwait(false);
									addin.GithubIssueUrl = new Uri(issue.Url);
									addin.GithubIssueId = issue.Number;
								}
								catch (ApiException e) when (e.ApiError.Message.EqualsIgnoreCase("Issues are disabled for this repo"))
								{
									// There's a NuGet package with a project URL that points to a fork which doesn't allow issue.
									// Therefore it's safe to ignore this error.
								}
								catch (ApiException e) when (e.ApiError.Message.EqualsIgnoreCase("Not Found"))
								{
									// I know of at least one case where the URL in the NuGet metadata points to a repo that has been deleted.
									// Therefore it's safe to ignore this error.
								}
								catch (Exception e)
								{
									Debugger.Break();
									throw;
								}
							}
						}

						return addin;
					}, Constants.MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);
		}
	}
}
