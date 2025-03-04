using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
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
		public bool PreConditionIsMet(DiscoveryContext context) => context.Options.SynchronizeContributors;

		public string GetDescription(DiscoveryContext context) => "Get the list of people who contributed to Cake";

		public async Task ExecuteAsync(DiscoveryContext context, TextWriter log, CancellationToken cancellationToken)
		{
			var apiOptions = new Octokit.ApiOptions() { PageSize = 500 };

			// Get the public repositories
			var cakeRepos = await context.GithubClient.Repository.GetAllForOrg(Constants.CAKE_REPO_OWNER, apiOptions).ConfigureAwait(false);
			var cakecontribRepos = await context.GithubClient.Repository.GetAllForOrg(Constants.CAKE_CONTRIB_REPO_OWNER, apiOptions).ConfigureAwait(false);
			var publicRepos = cakeRepos
				.Union(cakecontribRepos)
				.Where(repo => !repo.Private && !repo.Fork)
				.Where(repo => !context.ExcludedRepositories.Contains(repo.FullName)) // Ignore repositories on the exclusion list
				.ToArray();

			// Get the contributors for each repository
			var allContributors = new Dictionary<Repository, IEnumerable<RepositoryContributor>>(250);
			foreach (var publicRepo in publicRepos)
			{
				var repoContributors = await context.GithubClient.Repository.GetAllContributors(publicRepo.Id, false, apiOptions).ConfigureAwait(false);
				allContributors[publicRepo] = repoContributors;
			}

			// Sort alphabetically and extract the data that we want in the YAML and JSON files
			var contributors = allContributors
				.SelectMany(kvp => kvp.Value
					.Where(contributor => !contributor.Type.EqualsIgnoreCase("Bot")) // Ignore bots
					.Where(contributor => !context.ExcludedContributors.Contains(contributor.Login, StringComparer.OrdinalIgnoreCase)) // Ignore contributors on the exclusion list
					.Select(contributor => (Contributor: contributor, Repo: kvp.Key)))
				.GroupBy(
					x => x.Contributor,  // keySelector
					x => x.Repo,    // elementSelector
					(contributor, repos) => new
					{
						Name = contributor.Login,
						AvatarUrl = $"https://avatars.githubusercontent.com/u/{contributor.Id}", // https://github.com/cake-contrib/Cake.AddinDiscoverer/issues/236
						contributor.HtmlUrl,
						Repositories = repos.Select(repo => repo.FullName).ToArray()
					},  // resultSelector
					new KeyEqualityComparer<RepositoryContributor, long>(contributor => contributor.Id)) // comparer
				.OrderBy(x => x.Name)
				.ToArray();

			// Ensure the fork is up-to-date
			var fork = await context.GithubClient.CreateOrRefreshFork(Constants.CAKE_REPO_OWNER, Constants.CAKE_WEBSITE_REPO_NAME).ConfigureAwait(false);

			// Get the content of the current contributors files and generate the new content
			IReadOnlyList<RepositoryContent> directoryContent;

			try
			{
				directoryContent = await context.GithubClient.Repository.Content.GetAllContents(Constants.CAKE_REPO_OWNER, Constants.CAKE_WEBSITE_REPO_NAME, "contributors").ConfigureAwait(false);
			}
			catch (NotFoundException)
			{
				directoryContent = Array.Empty<RepositoryContent>();
			}

			var desiredContributorsFiles = new[] { "contributors.json", "contributors.yml" };
			var contributorFilesWithContent = await desiredContributorsFiles
				.ForEachAsync<string, (string Name, string CurrentContent, string NewContent)>(
					async fileName =>
					{
						var currentContent = string.Empty;
						if (directoryContent.Any(currentFile => currentFile.Name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)))
						{
							var contents = await context.GithubClient.Repository.Content.GetAllContents(Constants.CAKE_REPO_OWNER, Constants.CAKE_WEBSITE_REPO_NAME, $"contributors/{fileName}").ConfigureAwait(false);
							if (contents != null && contents.Count >= 1) currentContent = contents[0].Content;
						}

						var newContent = Path.GetExtension(fileName) switch
						{
							".yml" => contributors.ToYamlString("\n"),
							".json" => JsonSerializer.Serialize(contributors, contributors.GetType(), Misc.GetJsonOptions(true)),
							_ => throw new Exception($"Don't know how to generate the content of {fileName}")
						};

						return (fileName, currentContent, newContent);
					},
					Constants.MAX_GITHUB_CONCURENCY)
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
				var pullRequest = await Misc.CommitToNewBranchAndSubmitPullRequestAsync(context, fork, issue?.Number, newBranchName, Constants.CONTRIBUTORS_SYNCHRONIZATION_ISSUE_TITLE, commits).ConfigureAwait(false);
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
								Path: $"contributors/{fileToBeCreated.Name}",
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
								Path: $"contributors/{fileToBeUpdated.Name}",
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
