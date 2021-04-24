using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
using Octokit;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class CreateGithubIssueStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => context.Options.CreateGithubIssue;

		public string GetDescription(DiscoveryContext context) => "Create Github issues";

		public async Task ExecuteAsync(DiscoveryContext context, TextWriter log)
		{
			var recommendedCakeVersion = Constants.CAKE_VERSIONS
				.OrderByDescending(cakeVersion => cakeVersion.Version)
				.First();

			context.Addins = await context.Addins
				.ForEachAsync(
					async addin =>
					{
						if (addin.Type != AddinType.Recipe &&
							!addin.IsDeprecated &&
							!string.IsNullOrEmpty(addin.RepositoryName) &&
							!string.IsNullOrEmpty(addin.RepositoryOwner))
						{
							if (addin.AuditIssue != null && addin.AuditIssue.UpdatedAt.HasValue && DateTimeOffset.UtcNow.Subtract(addin.AuditIssue.UpdatedAt.Value).TotalDays > 90)
							{
								await AddCommentAsync(context.Options.DryRun, context, addin).ConfigureAwait(false);
							}
							else if (addin.AuditIssue == null)
							{
								var issue = await CreateIssueAsync(context.Options.DryRun, context, addin, recommendedCakeVersion).ConfigureAwait(false);
								if (issue != null)
								{
									addin.AuditIssue = issue;
									context.IssuesCreatedByCurrentUser.Add(issue);
								}
							}
						}

						return addin;
					}, Constants.MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);
		}

		private async Task<IssueComment> AddCommentAsync(bool debugging, DiscoveryContext context, AddinMetadata addin)
		{
			var comment = new StringBuilder();
			comment.AppendLine($"We performed a follow up automated audit of your Cake addin and found that some (or all) issues previously identified have not been resolved.{Environment.NewLine}");
			comment.AppendLine($"We strongly encourage you to make the modifications previously highlighted.{Environment.NewLine}");

			if (addin.AnalysisResult.Icon == IconAnalysisResult.RawgitUrl)
			{
				comment.AppendLine($"In particular would would like to highlight the fact that you use the rawgit CDN to serve your addin's icon. On October 8 2018 the maintainer of rawgit made the [announcement](https://rawgit.com/) that rawgit would shutdown in October 2019. Therefore it's **urgent** that you change your addin's icon URL to the new recommended URL: `{Constants.NEW_CAKE_CONTRIB_ICON_URL}`.{Environment.NewLine}");
			}

			if (addin.AnalysisResult.Icon != IconAnalysisResult.EmbeddedCakeContrib && addin.AnalysisResult.Icon != IconAnalysisResult.EmbeddedFancyCakeContrib)
			{
				comment.AppendLine($"Please also note that the recommendation changed following .netcore3.0's release: you should now embedded the icon in your Nuget package. Read more about embedded icons in the [.nuspec reference](https://docs.microsoft.com/en-us/nuget/reference/nuspec#icon).{Environment.NewLine}");
			}

			comment.AppendLine($"{Environment.NewLine}This comment was created by a tool: Cake.AddinDiscoverer version {context.Version}{Environment.NewLine}");

			IssueComment issueComment = null;

			try
			{
				if (debugging)
				{
					await File.WriteAllTextAsync(Path.Combine(context.TempFolder, $"Comment_{addin.Name}.txt"), comment.ToString()).ConfigureAwait(false);
				}
				else
				{
					issueComment = await context.GithubClient.Issue.Comment.Create(addin.RepositoryOwner, addin.RepositoryName, addin.AuditIssue.Number, comment.ToString()).ConfigureAwait(false);
				}
			}
			catch (ApiException e) when (e.ApiError.Message.EqualsIgnoreCase("Issues are disabled for this repo"))
			{
				// There's a NuGet package with a project URL that points to a fork which doesn't allow issue.
				// Therefore it's safe to ignore this error.
			}
			catch (ApiException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
			{
				// I know of at least one case where the URL in the NuGet metadata points to a repo that has been deleted.
				// Therefore it's safe to ignore this error.
			}
#pragma warning disable CS0168 // Variable is declared but never used
			catch (Exception e)
#pragma warning restore CS0168 // Variable is declared but never used
			{
				Debugger.Break();
				throw;
			}
			finally
			{
				// This delay is important to avoid triggering GitHub's abuse protection
				await Misc.RandomGithubDelayAsync().ConfigureAwait(false);
			}

			return issueComment;
		}

		private async Task<Issue> CreateIssueAsync(bool debugging, DiscoveryContext context, AddinMetadata addin, CakeVersion recommendedCakeVersion)
		{
			var issuesDescription = new StringBuilder();
			if (addin.AnalysisResult.CakeCoreVersion == Constants.UNKNOWN_VERSION)
			{
				issuesDescription.AppendLine($"- [ ] We were unable to determine what version of Cake.Core your addin is referencing. Please make sure you are referencing {recommendedCakeVersion.Version}");
			}
			else if (!addin.AnalysisResult.CakeCoreVersion.IsUpToDate(recommendedCakeVersion.Version))
			{
				issuesDescription.AppendLine($"- [ ] You are currently referencing Cake.Core {addin.AnalysisResult.CakeCoreVersion}. Please upgrade to {recommendedCakeVersion.Version}");
			}

			if (addin.AnalysisResult.CakeCommonVersion == Constants.UNKNOWN_VERSION)
			{
				issuesDescription.AppendLine($"- [ ] We were unable to determine what version of Cake.Common your addin is referencing. Please make sure you are referencing {recommendedCakeVersion.Version}");
			}
			else if (!addin.AnalysisResult.CakeCommonVersion.IsUpToDate(recommendedCakeVersion.Version))
			{
				issuesDescription.AppendLine($"- [ ] You are currently referencing Cake.Common {addin.AnalysisResult.CakeCommonVersion}. Please upgrade to {recommendedCakeVersion.Version}");
			}

			if (!addin.AnalysisResult.CakeCoreIsPrivate) issuesDescription.AppendLine($"- [ ] The Cake.Core reference should be private. Specifically, your addin's `.csproj` should have a line similar to this: `<PackageReference Include=\"Cake.Core\" Version=\"{recommendedCakeVersion.Version}\" PrivateAssets=\"All\" />`");
			if (!addin.AnalysisResult.CakeCommonIsPrivate) issuesDescription.AppendLine($"- [ ] The Cake.Common reference should be private. Specifically, your addin's `.csproj` should have a line similar to this: `<PackageReference Include=\"Cake.Common\" Version=\"{recommendedCakeVersion.Version}\" PrivateAssets=\"All\" />`");
			if (!Misc.IsFrameworkUpToDate(addin.Frameworks, recommendedCakeVersion)) issuesDescription.AppendLine($"- [ ] Your addin should target {recommendedCakeVersion.RequiredFramework} at a minimum. Optionally, your addin can also multi-target {string.Join(" or ", recommendedCakeVersion.OptionalFrameworks)}.");

			switch (addin.AnalysisResult.Icon)
			{
				case IconAnalysisResult.Unspecified:
				case IconAnalysisResult.RawgitUrl:
				case IconAnalysisResult.CustomUrl:
					issuesDescription.AppendLine("- [ ] The nuget package for your addin should embed the cake-contrib icon. Specifically, your addin's `.csproj` should have a line like this: `<PackageIcon>path/to/icon.png</PackageIcon>`.");
					break;
				case IconAnalysisResult.EmbeddedCustom:
					issuesDescription.AppendLine("- [ ] The icon embedded in your nuget package doesn't appear to be the cake-contrib icon. We strongly recommend that you use the icon [available here](https://github.com/cake-contrib/graphics/blob/master/png/cake-contrib-medium.png).");
					break;
			}

			if (issuesDescription.Length == 0) return null;

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

			Issue issue = null;

			try
			{
				if (debugging)
				{
					await File.WriteAllTextAsync(Path.Combine(context.TempFolder, $"Issue_{addin.Name}.txt"), issueBody.ToString()).ConfigureAwait(false);
				}
				else
				{
					issue = await context.GithubClient.Issue.Create(addin.RepositoryOwner, addin.RepositoryName, newIssue).ConfigureAwait(false);
				}
			}
			catch (ApiException e) when (e.ApiError.Message.EqualsIgnoreCase("Issues are disabled for this repo"))
			{
				// There's a NuGet package with a project URL that points to a fork which doesn't allow issues.
				// Therefore it's safe to ignore this error.
			}
			catch (ApiException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
			{
				// I know of at least one case where the URL in the NuGet metadata points to a repo that has been deleted.
				// Therefore it's safe to ignore this error.
			}
#pragma warning disable CS0168 // Variable is declared but never used
			catch (Exception e)
#pragma warning restore CS0168 // Variable is declared but never used
			{
				Debugger.Break();
				throw;
			}
			finally
			{
				// This delay is important to avoid triggering GitHub's abuse protection
				await Misc.RandomGithubDelayAsync().ConfigureAwait(false);
			}

			return issue;
		}
	}
}
