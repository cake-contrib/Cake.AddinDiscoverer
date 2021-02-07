using Cake.AddinDiscoverer.Models;
using Cake.Incubator.StringExtensions;
using Octokit;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Utilities
{
	internal static class Misc
	{
		public static bool IsFrameworkUpToDate(string[] currentFrameworks, CakeVersion desiredCakeVersion)
		{
			if (currentFrameworks == null || !currentFrameworks.Any()) return false;
			else if (!currentFrameworks.Contains(desiredCakeVersion.RequiredFramework, StringComparer.InvariantCultureIgnoreCase)) return false;

			var unnecessaryFrameworks = currentFrameworks
				.Except(new[] { desiredCakeVersion.RequiredFramework })
				.Except(desiredCakeVersion.OptionalFrameworks)
				.ToArray();
			if (unnecessaryFrameworks.Any()) return false;

			return true;
		}

		public static async Task<Issue> FindGithubIssueAsync(DiscoveryContext context, string repoOwner, string repoName, string creator, string title)
		{
			// Optimization: if the creator is the current user, we can rely on the cached list of issues
			if (creator.EqualsIgnoreCase(context.GithubClient.Connection.Credentials.Login))
			{
				return context.IssuesCreatedByCurrentUser
					.Where(i =>
					{
						var success = Misc.DeriveGitHubRepositoryInfo(new Uri(i.Url), out string owner, out string name);
						return owner.EqualsIgnoreCase(repoOwner) && name.EqualsIgnoreCase(repoName);
					})
					.FirstOrDefault(i => i.Title.EqualsIgnoreCase(title));
			}
			else
			{
				var request = new RepositoryIssueRequest()
				{
					Creator = creator,
					State = ItemStateFilter.Open,
					SortProperty = IssueSort.Created,
					SortDirection = SortDirection.Descending
				};

				var issues = await context.GithubClient.Issue.GetAllForRepository(repoOwner, repoName, request).ConfigureAwait(false);
				var issue = issues.FirstOrDefault(i => i.Title.EqualsIgnoreCase(title));
				return issue;
			}
		}

		public static async Task<PullRequest> FindGithubPullRequestAsync(DiscoveryContext context, string repoOwner, string repoName, string creator, string title)
		{
			// Optimization: if the creator is the current user, we can rely on the cached list of pull requests
			if (creator.EqualsIgnoreCase(context.GithubClient.Connection.Credentials.Login))
			{
				return context.PullRequestsCreatedByCurrentUser
					.Where(p =>
					{
						var success = Misc.DeriveGitHubRepositoryInfo(new Uri(p.Url), out string owner, out string name);
						return owner.EqualsIgnoreCase(repoOwner) && name.EqualsIgnoreCase(repoName);
					})
					.FirstOrDefault(i => i.Title.EqualsIgnoreCase(title));
			}
			else
			{
				var request = new PullRequestRequest()
				{
					State = ItemStateFilter.Open,
					SortProperty = PullRequestSort.Created,
					SortDirection = SortDirection.Descending
				};

				var pullRequests = await context.GithubClient.PullRequest.GetAllForRepository(repoOwner, repoName, request).ConfigureAwait(false);
				var pullRequest = pullRequests.FirstOrDefault(pr => pr.Title.EqualsIgnoreCase(title) && pr.User.Login.EqualsIgnoreCase(creator));

				return pullRequest;
			}
		}

		public static async Task<PullRequest> CommitToNewBranchAndSubmitPullRequestAsync(DiscoveryContext context, Octokit.Repository fork, int? issueNumber, string newBranchName, string pullRequestTitle, IEnumerable<(string CommitMessage, IEnumerable<string> FilesToDelete, IEnumerable<(EncodingType Encoding, string Path, string Content)> FilesToUpsert)> commits)
		{
			if (context.Options.DryRun)
			{
				await Misc.CommitToNewBranchAsync(context, fork, issueNumber, newBranchName, commits).ConfigureAwait(false);

				const string githubUrl = "https://github.com";
				var upstream = fork.Parent;
				Console.WriteLine($"DRY RUN - view diff here: {githubUrl}/{upstream.Owner.Login}/{upstream.Name}/compare/{upstream.DefaultBranch}...{fork.Owner.Login}:{newBranchName}");

				return null;
			}
			else
			{
				await Misc.CommitToNewBranchAsync(context, fork, issueNumber, newBranchName, commits).ConfigureAwait(false);
				var pullRequest = await Misc.SubmitPullRequestAsync(context, fork, issueNumber, newBranchName, pullRequestTitle).ConfigureAwait(false);
				return pullRequest;
			}
		}

		public static async Task<Commit> CommitToNewBranchAsync(DiscoveryContext context, Octokit.Repository fork, int? issueNumber, string newBranchName, IEnumerable<(string CommitMessage, IEnumerable<string> FilesToDelete, IEnumerable<(EncodingType Encoding, string Path, string Content)> FilesToUpsert)> commits)
		{
			if (commits == null || !commits.Any()) throw new ArgumentNullException(nameof(commits), "You must provide at least one commit");

			var defaultBranchReference = await context.GithubClient.Git.Reference.Get(context.Options.GithubUsername, fork.Name, $"heads/{fork.DefaultBranch}").ConfigureAwait(false);
			var newReference = new NewReference($"heads/{newBranchName}", defaultBranchReference.Object.Sha);
			var newBranch = await context.GithubClient.Git.Reference.Create(context.Options.GithubUsername, fork.Name, newReference).ConfigureAwait(false);

			var latestCommit = await context.GithubClient.Git.Commit.Get(context.Options.GithubUsername, fork.Name, newBranch.Object.Sha).ConfigureAwait(false);

			foreach (var (commitMessage, filesToDelete, filesToUpsert) in commits)
			{
				latestCommit = await context.GithubClient.ModifyFilesAsync(fork, latestCommit, filesToDelete, filesToUpsert, issueNumber.HasValue ? $"(GH-{issueNumber.Value}) {commitMessage}" : commitMessage).ConfigureAwait(false);
			}

			await context.GithubClient.Git.Reference.Update(fork.Owner.Login, fork.Name, $"heads/{newBranchName}", new ReferenceUpdate(latestCommit.Sha)).ConfigureAwait(false);

			return latestCommit;
		}

		public static async Task<PullRequest> SubmitPullRequestAsync(DiscoveryContext context, Octokit.Repository fork, int? issueNumber, string newBranchName, string pullRequestTitle)
		{
			var upstream = fork.Parent;

			var body = new StringBuilder();
			body.AppendFormat("This pull request was created by a tool: Cake.AddinDiscoverer version {0}", context.Version);
			if (issueNumber.HasValue)
			{
				body.AppendLine();
				body.AppendLine();
				body.AppendFormat("Resolves #{0}", issueNumber.Value);
			}

			var newPullRequest = new NewPullRequest(pullRequestTitle, $"{fork.Owner.Login}:{newBranchName}", upstream.DefaultBranch)
			{
				Body = body.ToString()
			};
			var pullRequest = await context.GithubClient.PullRequest.Create(upstream.Owner.Login, upstream.Name, newPullRequest).ConfigureAwait(false);

			return pullRequest;
		}

		public static async Task<IDictionary<string, Stream>> GetRepoContentAsync(DiscoveryContext context, AddinMetadata addin)
		{
			IDictionary<string, Stream> repoContent;

			var zipArchive = await context.GithubClient.Repository.Content.GetArchive(addin.RepositoryOwner, addin.RepositoryName, ArchiveFormat.Zipball).ConfigureAwait(false);
			using (var data = new MemoryStream(zipArchive))
			{
				using var archive = new ZipArchive(data);

				repoContent = archive.Entries
					.ToDictionary(item => item.FullName, item => item.Open());
			}

			return repoContent;
		}

		public static bool DeriveGitHubRepositoryInfo(Uri url, out string owner, out string name)
		{
			owner = string.Empty;
			name = string.Empty;

			if (url == null) return false;
			if (!url.IsGithubUrl()) return false;

			var parts = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length >= 3 && parts[0].EqualsIgnoreCase("repos"))
			{
				owner = parts[1];
				name = parts[2].TrimEnd(".git", StringComparison.OrdinalIgnoreCase);
				return true;
			}
			else if (parts.Length >= 2)
			{
				owner = parts[0];
				name = parts[1].TrimEnd(".git", StringComparison.OrdinalIgnoreCase);
				return true;
			}
			else
			{
				return false;
			}
		}

		// byte[] is implicitly convertible to ReadOnlySpan<byte>
		public static bool ByteArrayCompare(ReadOnlySpan<byte> a1, ReadOnlySpan<byte> a2)
		{
			return a1.SequenceEqual(a2);
		}

		public static async Task<string> GetCakeRecipeDotNetToolsConfig(DiscoveryContext context)
		{
			var contents = await context.GithubClient.Repository.Content.GetAllContents(Constants.CAKE_CONTRIB_REPO_OWNER, Constants.CAKE_RECIPE_REPO_NAME, Constants.DOT_NET_TOOLS_CONFIG_PATH).ConfigureAwait(false);
			return contents[0].Content;
		}

		public static Uri StandardizeGitHubUri(Uri originalUri)
		{
			if (originalUri == null) return null;
			if (!originalUri.IsGithubUrl()) return originalUri;

			// Force final slash except for repo URLs
			var path = originalUri.LocalPath.EndsWithIgnoreCase(".git")
				? originalUri.LocalPath
				: $"{originalUri.LocalPath.TrimEnd('/')}/";

			var standardizedUri = new UriBuilder(
					Uri.UriSchemeHttps, // Force HTTPS
					originalUri.Host,
					originalUri.IsDefaultPort ? -1 : originalUri.Port, // -1 => default port for scheme
					path,
					originalUri.Query)
				.Uri;

			return standardizedUri;
		}

		// This is to ensure we don't issue requests too quickly and therefore trigger Github's abuse detection
		// Randomize the delay to further minimize the likelihood of triggering Github's abuse detection
		public static Task RandomGithubDelayAsync()
		{
			return Task.Delay(RandomNumberGenerator.GetInt32(600, 2000));
		}
	}
}
