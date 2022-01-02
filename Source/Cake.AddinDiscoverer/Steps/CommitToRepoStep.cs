using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using Octokit;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class CommitToRepoStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => context.Options.MarkdownReportToRepo || context.Options.ExcelReportToRepo;

		public string GetDescription(DiscoveryContext context) => $"Committing changes to {Constants.CAKE_CONTRIB_REPO_OWNER}/{Constants.CAKE_CONTRIB_REPO_NAME} repo";

		public async Task ExecuteAsync(DiscoveryContext context, TextWriter log, CancellationToken cancellationToken)
		{
			// Get the SHA of the latest commit of the master branch.
			var headMasterRef = "heads/master";
			var masterReference = await context.GithubClient.Git.Reference.Get(Constants.CAKE_CONTRIB_REPO_OWNER, Constants.CAKE_CONTRIB_REPO_NAME, headMasterRef).ConfigureAwait(false); // Get reference of master branch
			var latestCommit = await context.GithubClient.Git.Commit.Get(Constants.CAKE_CONTRIB_REPO_OWNER, Constants.CAKE_CONTRIB_REPO_NAME, masterReference.Object.Sha).ConfigureAwait(false); // Get the laster commit of this branch
			var tree = new NewTree { BaseTree = latestCommit.Tree.Sha };

			// Create the blobs corresponding to the reports and add them to the tree
			if (context.Options.ExcelReportToRepo)
			{
				foreach (var excelReport in Directory.EnumerateFiles(context.TempFolder, $"*.xlsx"))
				{
					var excelBinary = await File.ReadAllBytesAsync(excelReport).ConfigureAwait(false);
					var excelReportBlob = new NewBlob
					{
						Encoding = EncodingType.Base64,
						Content = Convert.ToBase64String(excelBinary)
					};
					var excelReportBlobRef = await context.GithubClient.Git.Blob.Create(Constants.CAKE_CONTRIB_REPO_OWNER, Constants.CAKE_CONTRIB_REPO_NAME, excelReportBlob).ConfigureAwait(false);
					tree.Tree.Add(new NewTreeItem
					{
						Path = Path.GetFileName(excelReport),
						Mode = Constants.FILE_MODE,
						Type = TreeType.Blob,
						Sha = excelReportBlobRef.Sha
					});
				}
			}

			if (context.Options.MarkdownReportToRepo)
			{
				foreach (var markdownReport in Directory.EnumerateFiles(context.TempFolder, $"*.md"))
				{
					var makdownReportBlob = new NewBlob
					{
						Encoding = EncodingType.Utf8,
						Content = await File.ReadAllTextAsync(markdownReport).ConfigureAwait(false)
					};
					var makdownReportBlobRef = await context.GithubClient.Git.Blob.Create(Constants.CAKE_CONTRIB_REPO_OWNER, Constants.CAKE_CONTRIB_REPO_NAME, makdownReportBlob).ConfigureAwait(false);
					tree.Tree.Add(new NewTreeItem
					{
						Path = Path.GetFileName(markdownReport),
						Mode = Constants.FILE_MODE,
						Type = TreeType.Blob,
						Sha = makdownReportBlobRef.Sha
					});
				}
			}

			if (File.Exists(context.StatsSaveLocation))
			{
				var statsBlob = new NewBlob
				{
					Encoding = EncodingType.Utf8,
					Content = await File.ReadAllTextAsync(context.StatsSaveLocation).ConfigureAwait(false)
				};
				var statsBlobRef = await context.GithubClient.Git.Blob.Create(Constants.CAKE_CONTRIB_REPO_OWNER, Constants.CAKE_CONTRIB_REPO_NAME, statsBlob).ConfigureAwait(false);
				tree.Tree.Add(new NewTreeItem
				{
					Path = Path.GetFileName(context.StatsSaveLocation),
					Mode = Constants.FILE_MODE,
					Type = TreeType.Blob,
					Sha = statsBlobRef.Sha
				});
			}

			if (File.Exists(context.GraphSaveLocation))
			{
				var graphBinary = await File.ReadAllBytesAsync(context.GraphSaveLocation).ConfigureAwait(false);
				var graphBlob = new NewBlob
				{
					Encoding = EncodingType.Base64,
					Content = Convert.ToBase64String(graphBinary)
				};
				var graphBlobRef = await context.GithubClient.Git.Blob.Create(Constants.CAKE_CONTRIB_REPO_OWNER, Constants.CAKE_CONTRIB_REPO_NAME, graphBlob).ConfigureAwait(false);
				tree.Tree.Add(new NewTreeItem
				{
					Path = Path.GetFileName(context.GraphSaveLocation),
					Mode = Constants.FILE_MODE,
					Type = TreeType.Blob,
					Sha = graphBlobRef.Sha
				});
			}

			// Create a new tree
			var newTree = await context.GithubClient.Git.Tree.Create(Constants.CAKE_CONTRIB_REPO_OWNER, Constants.CAKE_CONTRIB_REPO_NAME, tree).ConfigureAwait(false);

			// Create the commit with the SHAs of the tree and the reference of master branch
			var newCommit = new NewCommit($"Automated addins audit {DateTime.UtcNow:yyyy-MM-dd} at {DateTime.UtcNow:HH:mm} UTC", newTree.Sha, masterReference.Object.Sha);
			var commit = await context.GithubClient.Git.Commit.Create(Constants.CAKE_CONTRIB_REPO_OWNER, Constants.CAKE_CONTRIB_REPO_NAME, newCommit).ConfigureAwait(false);

			// Update the reference of master branch with the SHA of the commit
			await context.GithubClient.Git.Reference.Update(Constants.CAKE_CONTRIB_REPO_OWNER, Constants.CAKE_CONTRIB_REPO_NAME, headMasterRef, new ReferenceUpdate(commit.Sha)).ConfigureAwait(false);
		}
	}
}
