using Cake.AddinDiscoverer.Utilities;
using Octokit;
using System;
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
			var latestCakeVersion = Constants.CAKE_VERSIONS
				.OrderByDescending(cakeVersion => cakeVersion.Version)
				.First();

			// 0.28.0 was the latest version of Cake when I last ran this step (June 2018)
			var cakeVersionUsedLastTime = Constants.CAKE_VERSIONS
				.Single(cakeVersion => cakeVersion.Version.Minor == 28);

			context.Addins = await context.Addins
				.ForEachAsync(
					async addin =>
					{
						if (addin.Type != AddinType.Recipes && addin.GithubRepoUrl != null)
						{
							var recommendedCakeVersion = addin.GithubIssueId.HasValue ? cakeVersionUsedLastTime : latestCakeVersion;

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

							if (addin.GithubIssueId.HasValue)
							{
								if (!addin.AnalysisResult.UsingOldCakeContribIcon && !addin.AnalysisResult.UsingNewCakeContribIcon) issuesDescription.AppendLine($"- [ ] The nuget package for your addin should use the cake-contrib icon. Specifically, your addin's `.csproj` should have a line like this: `<PackageIconUrl>{Constants.OLD_CAKE_CONTRIB_ICON_URL}</PackageIconUrl>`.");
							}
							else
							{
								if (!addin.AnalysisResult.UsingNewCakeContribIcon) issuesDescription.AppendLine($"- [ ] The nuget package for your addin should use the cake-contrib icon. Specifically, your addin's `.csproj` should have a line like this: `<PackageIconUrl>{Constants.NEW_CAKE_CONTRIB_ICON_URL}</PackageIconUrl>`.");
							}

							if (addin.GithubIssueId.HasValue && issuesDescription.Length == 0)
							{
								var newComment = $"We performed a follow up automated audit of your Cake addin and found that the issues we previously identified have been resolved. Thank you!{Environment.NewLine}{Environment.NewLine}";
								newComment += "Please be aware that some of the recommendations we made in our last audit (which took place in June 2018) have changed. ";
								newComment += $"For instance, we now recommend that addins use Cake {latestCakeVersion.Version.ToString(3)} (rather than {cakeVersionUsedLastTime.Version.ToString(3)}) and also, due to the [announced demise of the rawgit CDN](https://rawgit.com/), we ask that you use a [new icon URL]({Constants.NEW_CAKE_CONTRIB_ICON_URL}).{Environment.NewLine}{Environment.NewLine}";
								newComment += $"All this to say that a future automated audit may create a new issue if you haven't already addressed these new recommendations.{Environment.NewLine}";
								var issueComent = await context.GithubClient.Issue.Comment.Create(addin.GithubRepoOwner, addin.GithubRepoName, addin.GithubIssueId.Value, newComment).ConfigureAwait(false);

								var issueUpdate = new IssueUpdate()
								{
									State = ItemState.Closed
								};
								var issue = await context.GithubClient.Issue.Update(addin.GithubRepoOwner, addin.GithubRepoName, addin.GithubIssueId.Value, issueUpdate).ConfigureAwait(false);
							}
							else if (issuesDescription.Length > 0)
							{
								if (addin.GithubIssueId.HasValue)
								{
									// Add a comment if we haven't done so already
									var comments = await context.GithubClient.Issue.Comment.GetAllForIssue(addin.GithubRepoOwner, addin.GithubRepoName, addin.GithubIssueId.Value, ApiOptions.None).ConfigureAwait(false);
									if (!comments.Any(c => c.Body.StartsWith("We performed a follow up automated audit")))
									{
										var newComment = $"We performed a follow up automated audit of your Cake addin and found that some (or all) issues previously identified have not been resolved.{Environment.NewLine}{Environment.NewLine}";
										newComment += $"We strongly encourage you to make the modifications previously highlighted.{Environment.NewLine}";
										newComment += $"{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}";
										newComment += $"Apologies if this is already being worked on, or if there are existing open issues, this issue was created based on what is currently published for this package on NuGet.{Environment.NewLine}";
										newComment += $"{Environment.NewLine}This comment was created by a tool: Cake.AddinDiscoverer version {context.Version}{Environment.NewLine}";
										var issueComment = await context.GithubClient.Issue.Comment.Create(addin.GithubRepoOwner, addin.GithubRepoName, addin.GithubIssueId.Value, newComment).ConfigureAwait(false);
									}
								}
								else
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

									var issue = await context.GithubClient.Issue.Create(addin.GithubRepoOwner, addin.GithubRepoName, newIssue).ConfigureAwait(false);
									addin.GithubIssueUrl = new Uri(issue.Url);
									addin.GithubIssueId = issue.Number;
								}
							}
						}

						return addin;
					}, Constants.MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);
		}
	}
}
