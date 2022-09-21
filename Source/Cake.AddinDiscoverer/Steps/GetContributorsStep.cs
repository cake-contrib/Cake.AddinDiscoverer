using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using Octokit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class GetContributorsStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => true;

		public string GetDescription(DiscoveryContext context) => "Get the list of people who contributed to Cake";

		public async Task ExecuteAsync(DiscoveryContext context, TextWriter log, CancellationToken cancellationToken)
		{
			var apiOptions = new Octokit.ApiOptions() { PageSize = 500 };

			// Get all the public repositories
			var repos = await context.GithubClient.Repository.GetAllForOrg("cake-build", apiOptions).ConfigureAwait(false);
			var publicRepos = repos.Where(repo => !repo.Private).ToArray();

			// Get the contributors for each repository
			var allContributors = new List<RepositoryContributor>(250);
			foreach (var publicRepo in publicRepos)
			{
				var repoContributors = await context.GithubClient.Repository.GetAllContributors(publicRepo.Id, false, apiOptions).ConfigureAwait(false);
				allContributors.AddRange(repoContributors);
			}

			// Remove duplicates and sort alphabetically
			var contributors = allContributors
				.DistinctBy(contributor => contributor.Id)
				.OrderBy(contributor => contributor.Login)
				.Select(contributor => new
				{
					Name = contributor.Login,
					AvatarUrl = contributor.AvatarUrl,
					HtmlUrl = contributor.HtmlUrl,
				})
				.ToArray();

			// Ensure the fork is up-to-date
			var fork = await context.GithubClient.CreateOrRefreshFork(Constants.CAKE_REPO_OWNER, Constants.CAKE_WEBSITE_REPO_NAME).ConfigureAwait(false);

			// Get the content of the current contributors files and generate the new content
			var directoryContent = await context.GithubClient.Repository.Content.GetAllContents(Constants.CAKE_REPO_OWNER, Constants.CAKE_WEBSITE_REPO_NAME, "maintainers").ConfigureAwait(false);
			var desiredContributorsFiles = new[] { "contributors.json", "contributors.yml" };
			var contributorFilesWithContent = await desiredContributorsFiles
				.ForEachAsync<string, (string Name, string CurrentContent, string NewContent)>(
					async fileName =>
					{
						var currentContent = string.Empty;
						if (directoryContent.Any(currentFile => currentFile.Name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)))
						{
							var contents = await context.GithubClient.Repository.Content.GetAllContents(Constants.CAKE_REPO_OWNER, Constants.CAKE_WEBSITE_REPO_NAME, $"maintainers/{fileName}").ConfigureAwait(false);
							currentContent = contents.FirstOrDefault()?.Content;
						}

						var newContent = Path.GetExtension(fileName) switch
						{
							".yml" => contributors.ToYamlString("\n"),
							".json" => JsonSerializer.Serialize(contributors, contributors.GetType(), new JsonSerializerOptions { WriteIndented = true }),
							_ => throw new Exception($"Don't know how to generate the content of {fileName}")
						};

						return (fileName, currentContent, newContent);
					}, Constants.MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);

			// Determine which files need to be created/updated
			var filesToBeCreated = contributorFilesWithContent
				.Where(file => string.IsNullOrEmpty(file.CurrentContent))
				.OrderBy(file => file.Name)
				.ToArray();
			var filesToBeUpdated = contributorFilesWithContent
				.Where(file => !string.IsNullOrEmpty(file.CurrentContent) && file.CurrentContent != file.NewContent)
				.OrderBy(file => file.Name)
				.ToArray();

			if (context.Options.DryRun)
			{
				// All changes created in a single branch and we don't create issues + PRs
				await DryRunAsync(context, fork, filesToBeCreated, filesToBeUpdated).ConfigureAwait(false);
			}
			else
			{
				// Check if an issue already exists
				var upstream = fork.Parent;
				var issue = await Misc.FindGithubIssueAsync(context, upstream.Owner.Login, upstream.Name, context.Options.GithubUsername, Constants.CONTRIBUTORS_SYNCHRONIZATION_ISSUE_TITLE).ConfigureAwait(false);

				if (issue != null)
				{
					return;
				}
				else
				{
					// Changes are committed to a single branch, one single issue is raised and a single PRs is opened
					await SynchronizeFilesCollectivelyAsync(context, fork, filesToBeCreated, filesToBeUpdated).ConfigureAwait(false);
				}
			}
		}

		private static async Task DryRunAsync(DiscoveryContext context, Repository fork, (string Name, string CurrentContent, string NewContent)[] filesToBeCreated, (string Name, string CurrentContent, string NewContent)[] filesToBeUpdated)
		{
			var newBranchName = $"dryrun_contributors_{DateTime.UtcNow:yyyy_MM_dd_HH_mm_ss}";
			var commits = ConvertToCommits(filesToBeCreated, filesToBeUpdated);

			if (commits.Any())
			{
				var apiInfo = context.GithubClient.GetLastApiInfo();
				var requestsLeft = apiInfo?.RateLimit?.Remaining ?? 0;

				if (requestsLeft < Constants.MIN_GITHUB_REQUESTS_THRESHOLD)
				{
					Console.WriteLine($"CONTRIBUTORS DRY RUN - Only {requestsLeft} GitHub API requests left. Therefore skipping Dry run");
				}
				else
				{
					await Misc.CommitToNewBranchAsync(context, fork, null, newBranchName, commits).ConfigureAwait(false);

					const string githubUrl = "https://github.com";
					Console.WriteLine($"CONTRIBUTORS DRY RUN - view diff here: {githubUrl}/{fork.Parent.Owner.Login}/{fork.Parent.Name}/compare/{fork.Parent.DefaultBranch}...{fork.Owner.Login}:{newBranchName}");
				}
			}
			else
			{
				Console.WriteLine("CONTRIBUTORS DRY RUN - no files to create or modify");
			}
		}

		private static async Task SynchronizeFilesCollectivelyAsync(DiscoveryContext context, Repository fork, (string Name, string CurrentContent, string NewContent)[] filesToBeCreated, (string Name, string CurrentContent, string NewContent)[] filesToBeUpdated)
		{
			var apiInfo = context.GithubClient.GetLastApiInfo();
			var requestsLeft = apiInfo?.RateLimit?.Remaining ?? 0;

			var threshold = Math.Max(Constants.MIN_GITHUB_REQUESTS_THRESHOLD, (filesToBeCreated.Length + filesToBeUpdated.Length) * 3);
			if (requestsLeft < threshold)
			{
				Console.WriteLine($"  Only {requestsLeft} GitHub API requests left. Therefore skipping contributors files synchronization.");
				return;
			}

			var upstream = fork.Parent;

			// Convert files into commits
			var newBranchName = $"contributors_files_sync_{DateTime.UtcNow:yyyy_MM_dd_HH_mm_ss}";
			var commits = ConvertToCommits(filesToBeCreated, filesToBeUpdated);

			if (commits.Any())
			{
				// Create issue
				var newIssue = new NewIssue(Constants.CONTRIBUTORS_SYNCHRONIZATION_ISSUE_TITLE)
				{
					Body = $"The Cake.AddinDiscoverer tool has discovered that the list of contributors has changed.{Environment.NewLine}"
				};
				var issue = await context.GithubClient.Issue.Create(Constants.CAKE_REPO_OWNER, Constants.CAKE_WEBSITE_REPO_NAME, newIssue).ConfigureAwait(false);
				context.IssuesCreatedByCurrentUser.Add(issue);

				// Commit changes to a new branch and submit PR
				var pullRequest = await Misc.CommitToNewBranchAndSubmitPullRequestAsync(context, fork, issue?.Number, newBranchName, Constants.COLLECTIVE_YAML_SYNCHRONIZATION_ISSUE_TITLE, commits).ConfigureAwait(false);
				if (pullRequest != null) context.PullRequestsCreatedByCurrentUser.Add(pullRequest);
			}
		}

		private static IEnumerable<(string CommitMessage, IEnumerable<string> FilesToDelete, IEnumerable<(EncodingType Encoding, string Path, string Content)> FilesToUpsert)> ConvertToCommits((string Name, string CurrentContent, string NewContent)[] filesToBeCreated, (string Name, string CurrentContent, string NewContent)[] filesToBeUpdated)
		{
#pragma warning disable SA1009 // Closing parenthesis should be spaced correctly

			if (filesToBeCreated.Any())
			{
				foreach (var fileToBeCreated in filesToBeCreated)
				{
					yield return
					(
						CommitMessage: $"Create {fileToBeCreated.Name}",
						FilesToDelete: null,
						FilesToUpsert: new[]
						{
							(
								Encoding: EncodingType.Utf8,
								Path: $"maintainers/{fileToBeCreated.Name}",
								Content: fileToBeCreated.NewContent
							)
						}
					);
				}
			}

			if (filesToBeUpdated.Any())
			{
				foreach (var fileToBeUpdated in filesToBeUpdated)
				{
					yield return
					(
						CommitMessage: $"Update {fileToBeUpdated.Name}",
						FilesToDelete: null,
						FilesToUpsert: new[]
						{
							(
								Encoding: EncodingType.Utf8,
								Path: $"maintainers/{fileToBeUpdated.Name}",
								Content: fileToBeUpdated.NewContent
							)
						}
					);
				}
#pragma warning restore SA1009 // Closing parenthesis should be spaced correctly
			}
		}
	}
}
