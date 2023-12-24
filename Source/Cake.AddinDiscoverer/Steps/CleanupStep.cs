using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using Octokit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class CleanupStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => true;

		public string GetDescription(DiscoveryContext context) => $"Clean up {context.TempFolder}";

		public async Task ExecuteAsync(DiscoveryContext context, TextWriter log, CancellationToken cancellationToken)
		{
			await ClearCacheAndOutputFolders(context, log, cancellationToken).ConfigureAwait(false);
			await DeleteBranches(context, log).ConfigureAwait(false);
		}

		private static async Task ClearCacheAndOutputFolders(DiscoveryContext context, TextWriter log, CancellationToken cancellationToken)
		{
			if (context.Options.ClearCache && Directory.Exists(context.TempFolder))
			{
				Directory.Delete(context.TempFolder, true);
				await Task.Delay(500, cancellationToken).ConfigureAwait(false);
			}

			if (!Directory.Exists(context.TempFolder))
			{
				Directory.CreateDirectory(context.TempFolder);
				await Task.Delay(500, cancellationToken).ConfigureAwait(false);
			}

			if (!Directory.Exists(context.PackagesFolder))
			{
				Directory.CreateDirectory(context.PackagesFolder);
				await Task.Delay(500, cancellationToken).ConfigureAwait(false);
			}

			if (!Directory.Exists(context.ZipArchivesFolder))
			{
				Directory.CreateDirectory(context.ZipArchivesFolder);
				await Task.Delay(500, cancellationToken).ConfigureAwait(false);
			}

			if (!Directory.Exists(context.AnalysisFolder))
			{
				Directory.CreateDirectory(context.AnalysisFolder);
				await Task.Delay(500, cancellationToken).ConfigureAwait(false);
			}

			if (File.Exists(context.ExcelReportPath)) File.Delete(context.ExcelReportPath);
			if (File.Exists(context.MarkdownReportPath)) File.Delete(context.MarkdownReportPath);
			if (File.Exists(context.StatsSaveLocation)) File.Delete(context.StatsSaveLocation);
			if (File.Exists(context.GraphSaveLocation)) File.Delete(context.GraphSaveLocation);

			foreach (var markdownReport in Directory.EnumerateFiles(context.TempFolder, $"{Path.GetFileNameWithoutExtension(context.MarkdownReportPath)}*.md"))
			{
				File.Delete(markdownReport);
			}
		}

		private static async Task DeleteBranches(DiscoveryContext context, TextWriter log)
		{
			var sensitiveBranches = new[]
			{
				"develop",
				"master",
				"main",
				"publish/develop",
				"publish/master",
				"publish/main"
			};

			var branches = await context.GithubClient.Repository.Branch
				.GetAll(context.Options.GithubUsername, Constants.CAKE_WEBSITE_REPO_NAME)
				.ConfigureAwait(false);

			var safeBranches = branches
				.Where(branch => !sensitiveBranches.Contains(branch.Name))
				.ToArray();

			var dryRunBranches = safeBranches
				.Where(branch => branch.Name.StartsWith("dryrun", StringComparison.OrdinalIgnoreCase))
				.ToArray();

			var otherBranches = safeBranches
				.Except(dryRunBranches)
				.ToArray();

			await DeleteDryRunBranches(context, dryRunBranches, log).ConfigureAwait(false);
			await DeleteMergedBranches(context, otherBranches, log).ConfigureAwait(false);
		}

		private static async Task DeleteDryRunBranches(DiscoveryContext context, IEnumerable<Branch> branches, TextWriter log)
		{
			// Delete dry runs after a "reasonable" amount of time (60 days seems reasonable to me).
			foreach (var branch in branches)
			{
				var dateParts = branch.Name
					.Split('_')
					.Where(part => part.All(char.IsDigit))
					.Select(part => int.Parse(part))
					.ToArray();
				var createdOn = new DateTime(dateParts[0], dateParts[1], dateParts[2], dateParts[3], dateParts[4], dateParts[5], DateTimeKind.Utc);

				if (DateTime.UtcNow - createdOn > TimeSpan.FromDays(60))
				{
					await log.WriteLineAsync($"Deleting branch {context.Options.GithubUsername}/{Constants.CAKE_WEBSITE_REPO_NAME}/{branch.Name}").ConfigureAwait(false);
					await context.GithubClient.Git.Reference
						.Delete(context.Options.GithubUsername, Constants.CAKE_WEBSITE_REPO_NAME, $"heads/{branch.Name}")
						.ConfigureAwait(false);
				}
			}
		}

		private static async Task DeleteMergedBranches(DiscoveryContext context, IEnumerable<Branch> branches, TextWriter log)
		{
			// Delete branches when their corresponding PR has been merged
			foreach (var branch in branches)
			{
				var pullRequestsRequest = new PullRequestRequest()
				{
					State = ItemStateFilter.Closed,
					SortProperty = PullRequestSort.Updated,
					SortDirection = SortDirection.Descending,
					Head = $"{context.Options.GithubUsername}:{branch.Name}"
				};
				var pullRequests = await context.GithubClient.Repository.PullRequest
					.GetAllForRepository(Constants.CAKE_REPO_OWNER, Constants.CAKE_WEBSITE_REPO_NAME, pullRequestsRequest)
					.ConfigureAwait(false);
				var pr = pullRequests.SingleOrDefault(pr => pr.Head.Sha == branch.Commit.Sha);

				if (pr != null && pr.Merged)
				{
					await log.WriteLineAsync($"Deleting branch {context.Options.GithubUsername}/{Constants.CAKE_WEBSITE_REPO_NAME}/{branch.Name}").ConfigureAwait(false);
					await context.GithubClient.Git.Reference
						.Delete(context.Options.GithubUsername, Constants.CAKE_WEBSITE_REPO_NAME, $"heads/{branch.Name}")
						.ConfigureAwait(false);
				}
			}
		}
	}
}
