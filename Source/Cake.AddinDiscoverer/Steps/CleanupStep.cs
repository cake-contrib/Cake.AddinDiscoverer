using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using Octokit;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class CleanupStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => true;

		public string GetDescription(DiscoveryContext context) => $"Clean up {context.TempFolder}";

		public async Task ExecuteAsync(DiscoveryContext context, TextWriter log)
		{
			await ClearCacheAndOutputFolders(context, log).ConfigureAwait(false);
			await DeleteMergedBranches(context, log).ConfigureAwait(false);
		}

		private async Task ClearCacheAndOutputFolders(DiscoveryContext context, TextWriter log)
		{
			if (context.Options.ClearCache && Directory.Exists(context.TempFolder))
			{
				Directory.Delete(context.TempFolder, true);
				await Task.Delay(500).ConfigureAwait(false);
			}

			if (!Directory.Exists(context.TempFolder))
			{
				Directory.CreateDirectory(context.TempFolder);
				await Task.Delay(500).ConfigureAwait(false);
			}

			if (!Directory.Exists(context.PackagesFolder))
			{
				Directory.CreateDirectory(context.PackagesFolder);
				await Task.Delay(500).ConfigureAwait(false);
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

		private async Task DeleteMergedBranches(DiscoveryContext context, TextWriter log)
		{
			var request = new PullRequestRequest()
			{
				State = ItemStateFilter.Closed,
				SortProperty = PullRequestSort.Updated,
				SortDirection = SortDirection.Ascending
			};

			var branches = await context.GithubClient.Repository.Branch.GetAll(context.Options.GithubUsername, Constants.CAKE_WEBSITE_REPO_NAME).ConfigureAwait(false);
			var pullRequests = await context.GithubClient.Repository.PullRequest.GetAllForRepository(Constants.CAKE_REPO_OWNER, Constants.CAKE_WEBSITE_REPO_NAME, request).ConfigureAwait(false);

			foreach (var branch in branches)
			{
				var pr = pullRequests.SingleOrDefault(pr => pr.Head.Sha == branch.Commit.Sha);

				if (pr != null && pr.Merged)
				{
					await context.GithubClient.Git.Reference
						.Delete("cake-contrib-bot", Constants.CAKE_WEBSITE_REPO_NAME, $"heads/{branch.Name}")
						.ConfigureAwait(false);
				}
			}
		}
	}
}
