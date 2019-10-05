using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using NuGet.Common;
using NuGet.Protocol.Core.Types;
using Octokit;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Cake.AddinDiscoverer.Steps
{
	internal class UpdateCakeRecipeStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => context.Options.UpdateCakeRecipeReferences;

		public string GetDescription(DiscoveryContext context) => "Update Cake.Recipe";

		public async Task ExecuteAsync(DiscoveryContext context)
		{
			var cakeVersionUsedByRecipe = await FindCakeVersionUsedByRecipe(context).ConfigureAwait(false);
			await UpdateCakeRecipeAsync(context, cakeVersionUsedByRecipe).ConfigureAwait(false);
		}

		private async Task<SemVersion> FindCakeVersionUsedByRecipe(DiscoveryContext context)
		{
			var contents = await context.GithubClient.Repository.Content.GetAllContents(Constants.CAKE_CONTRIB_REPO_OWNER, Constants.CAKE_RECIPE_REPO_NAME, Constants.PACKAGES_CONFIG_PATH).ConfigureAwait(false);

			var xmlDoc = new XmlDocument();
			xmlDoc.LoadXml(contents[0].Content);

			var cakeVersion = xmlDoc.SelectSingleNode("/packages/package[@id=\"Cake\"]/@version").InnerText;
			return SemVersion.Parse(cakeVersion);
		}

		private async Task UpdateCakeRecipeAsync(DiscoveryContext context, SemVersion cakeVersionUsedInRecipe)
		{
			var allReferencedAddins = new ConcurrentDictionary<string, IEnumerable<(string Name, string CurrentVersion, string NewVersion)>>();

			var currentCakeVersion = Constants.CAKE_VERSIONS.Where(v => v.Version <= cakeVersionUsedInRecipe).Max();
			var nextCakeVersion = Constants.CAKE_VERSIONS.Where(v => v.Version > cakeVersionUsedInRecipe).Min();
			var latestCakeVersion = Constants.CAKE_VERSIONS.Where(v => v.Version > cakeVersionUsedInRecipe).Max();

			// Get the ".cake" files
			var recipeFiles = await GetRecipeFilesAsync(context, currentCakeVersion, nextCakeVersion, latestCakeVersion).ConfigureAwait(false);

			// Submit a PR if any addin reference is outdated
			await UpdateOutdatedRecipeFilesAsync(context, recipeFiles).ConfigureAwait(false);

			// Either submit a PR to upgrade to the latest version of Cake OR create an issue explaining why Cake.Recipe cannot be upgraded to latest Cake version
			if (latestCakeVersion != null && currentCakeVersion.Version < latestCakeVersion.Version)
			{
				await UpgradeCakeVersionUsedByRecipeAsync(context, recipeFiles, latestCakeVersion).ConfigureAwait(false);
			}
		}

		private async Task<RecipeFile[]> GetRecipeFilesAsync(DiscoveryContext context, CakeVersion currentCakeVersion, CakeVersion nextCakeVersion, CakeVersion latestCakeVersion)
		{
			var directoryContent = await context.GithubClient.Repository.Content.GetAllContents(Constants.CAKE_CONTRIB_REPO_OWNER, Constants.CAKE_RECIPE_REPO_NAME, "Cake.Recipe/Content").ConfigureAwait(false);
			var cakeFiles = directoryContent.Where(c => c.Type == new StringEnum<ContentType>(ContentType.File) && c.Name.EndsWith(".cake", StringComparison.OrdinalIgnoreCase));

			var recipeFiles = await cakeFiles
				.ForEachAsync(
					async cakeFile =>
					{
						var contents = await context.GithubClient.Repository.Content.GetAllContents(Constants.CAKE_CONTRIB_REPO_OWNER, Constants.CAKE_RECIPE_REPO_NAME, cakeFile.Path).ConfigureAwait(false);

						var recipeFile = new RecipeFile()
						{
							Name = cakeFile.Name,
							Path = cakeFile.Path,
							Content = contents[0].Content
						};

						foreach (var addinReference in recipeFile.AddinReferences)
						{
							addinReference.LatestVersionForCurrentCake = context.Addins.SingleOrDefault(addin =>
							{
								return addin.Name.Equals(addinReference.Name, StringComparison.OrdinalIgnoreCase) &&
									!addin.IsPrerelease &&
									(currentCakeVersion == null || (addin.AnalysisResult.CakeCoreVersion.IsUpToDate(currentCakeVersion.Version) && addin.AnalysisResult.CakeCommonVersion.IsUpToDate(currentCakeVersion.Version))) &&
									(nextCakeVersion == null || !(addin.AnalysisResult.CakeCoreVersion.IsUpToDate(nextCakeVersion.Version) && addin.AnalysisResult.CakeCommonVersion.IsUpToDate(nextCakeVersion.Version)));
							})?.NuGetPackageVersion;

							addinReference.LatestVersionForLatestCake = context.Addins.SingleOrDefault(addin =>
							{
								return addin.Name.Equals(addinReference.Name, StringComparison.OrdinalIgnoreCase) &&
									!addin.IsPrerelease &&
									(latestCakeVersion != null && (addin.AnalysisResult.CakeCoreVersion.IsUpToDate(latestCakeVersion.Version) && addin.AnalysisResult.CakeCommonVersion.IsUpToDate(latestCakeVersion.Version)));
							})?.NuGetPackageVersion;
						}

						var nugetPackageMetadataClient = context.NugetRepository.GetResource<PackageMetadataResource>();
						await recipeFile.ToolReferences.ForEachAsync(
							async toolReference =>
							{
								var searchMetadata = await nugetPackageMetadataClient.GetMetadataAsync(toolReference.Name, false, false, new SourceCacheContext(), NullLogger.Instance, CancellationToken.None).ConfigureAwait(false);
								var mostRecentPackageMetadata = searchMetadata.OrderByDescending(p => p.Published).FirstOrDefault();
								if (mostRecentPackageMetadata != null)
								{
									toolReference.LatestVersion = mostRecentPackageMetadata.Identity.Version.ToNormalizedString();
								}
							}, Constants.MAX_NUGET_CONCURENCY)
						.ConfigureAwait(false);

						return recipeFile;
					}, Constants.MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);

			return recipeFiles;
		}

		private async Task UpdateOutdatedRecipeFilesAsync(DiscoveryContext context, RecipeFile[] recipeFiles)
		{
			// Make sure there is at least one outdated reference
			var outdatedReferences = recipeFiles
				.SelectMany(recipeFile => recipeFile.AddinReferences
					.Where(r => !string.IsNullOrEmpty(r.LatestVersionForCurrentCake) && r.ReferencedVersion != r.LatestVersionForCurrentCake)
					.Select(r => new { Recipe = recipeFile, Reference = (CakeReference)r, Type = "addin", LatestVersion = r.LatestVersionForCurrentCake }))
				.Concat(recipeFiles
					.SelectMany(recipeFile => recipeFile.ToolReferences
						.Where(r => !string.IsNullOrEmpty(r.LatestVersion) && r.ReferencedVersion != r.LatestVersion)
						.Select(r => new { Recipe = recipeFile, Reference = (CakeReference)r, Type = "tool", r.LatestVersion })))
				.ToArray();
			if (!outdatedReferences.Any()) return;

			// Ensure the fork is up-to-date
			var fork = await context.GithubClient.RefreshFork(context.Options.GithubUsername, Constants.CAKE_RECIPE_REPO_NAME).ConfigureAwait(false);
			var upstream = fork.Parent;

			// Create an issue and PR for each outdated reference
			await outdatedReferences
				.ForEachAsync(
					async outdatedReference =>
					{
						// Check if an issue already exists
						var issueTitle = $"Reference to {outdatedReference.Type} {outdatedReference.Reference.Name} in {outdatedReference.Recipe.Name} needs to be updated";
						var issue = await Misc.FindGithubIssueAsync(context, upstream.Owner.Login, upstream.Name, context.Options.GithubUsername, issueTitle).ConfigureAwait(false);
						if (issue != null) return;

						// Create the issue
						var newIssue = new NewIssue(issueTitle)
						{
							Body = $"Reference to {outdatedReference.Reference.Name} {outdatedReference.Reference.ReferencedVersion} in {outdatedReference.Recipe.Name} should be updated to {outdatedReference.LatestVersion}"
						};
						issue = await context.GithubClient.Issue.Create(upstream.Owner.Login, upstream.Name, newIssue).ConfigureAwait(false);

						// Commit changes to a new branch and submit PR
						var commitMessageShort = $"Update {outdatedReference.Reference.Name} reference to {outdatedReference.LatestVersion}";
						var commitMessageLong = $"Update {outdatedReference.Reference.Name} reference from {outdatedReference.Reference.ReferencedVersion} to {outdatedReference.LatestVersion}";
						var newBranchName = $"update_{outdatedReference.Reference.Name.Replace('/', '_').Replace('.', '_').Replace('\\', '_')}_reference_{DateTime.UtcNow:yyyy_MM_dd_HH_mm_ss}";
						var commits = new List<(string CommitMessage, IEnumerable<string> FilesToDelete, IEnumerable<(EncodingType Encoding, string Path, string Content)> FilesToUpsert)>
						{
							(CommitMessage: commitMessageLong, FilesToDelete: null, FilesToUpsert: new[] { (EncodingType: EncodingType.Utf8, outdatedReference.Recipe.Path, Content: outdatedReference.Recipe.GetContentForCurrentCake(outdatedReference.Reference)) })
						};

						await Misc.CommitToNewBranchAndSubmitPullRequestAsync(context, fork, issue.Number, newBranchName, commitMessageShort, commits).ConfigureAwait(false);
					}, Constants.MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);
		}

		private async Task UpgradeCakeVersionUsedByRecipeAsync(DiscoveryContext context, RecipeFile[] recipeFiles, CakeVersion latestCakeVersion)
		{
			var recipeFilesWithAtLeastOneReference = recipeFiles
				.Where(recipeFile => recipeFile.AddinReferences.Any())
				.ToArray();
			if (!recipeFilesWithAtLeastOneReference.Any()) return;

			// Ensure the fork is up-to-date
			var fork = await context.GithubClient.RefreshFork(context.Options.GithubUsername, Constants.CAKE_RECIPE_REPO_NAME).ConfigureAwait(false);
			var upstream = fork.Parent;

			// The content of the issue body
			var issueBody = new StringBuilder();
			issueBody.AppendFormat("In order for Cake.Recipe to be compatible with Cake version {0}, each and every referenced addin must support Cake {0}. ", latestCakeVersion.Version.ToString(3));
			issueBody.AppendFormat("This issue will be used to track the full list of addins referenced in Cake.Recipe and whether or not they support Cake {0}. ", latestCakeVersion.Version.ToString(3));
			issueBody.AppendFormat("When all referenced addins are upgraded to support Cake {0}, we will automatically submit a PR to upgrade Cake.Recipe. ", latestCakeVersion.Version.ToString(3));
			issueBody.AppendFormat("In the mean time, this issue will be regularly updated when addins are updated with Cake {0} support.{1}", latestCakeVersion.Version.ToString(3), Environment.NewLine);
			issueBody.AppendLine();
			issueBody.AppendLine("Referenced Addins:");
			foreach (var recipeFile in recipeFilesWithAtLeastOneReference)
			{
				issueBody.AppendLine();
				issueBody.AppendLine($"- `{recipeFile.Name}` references the following addins:");
				foreach (var addinReference in recipeFile.AddinReferences)
				{
					issueBody.AppendLine($"    - [{(!string.IsNullOrEmpty(addinReference.LatestVersionForLatestCake) ? "x" : " ")}] {addinReference.Name}");
				}
			}

			// Create a new issue or update existing one
			var issueTitle = string.Format(Constants.CAKE_RECIPE_UPGRADE_CAKE_VERSION_ISSUE_TITLE, latestCakeVersion.Version.ToString(3));
			var issue = await Misc.FindGithubIssueAsync(context, upstream.Owner.Login, upstream.Name, context.Options.GithubUsername, issueTitle).ConfigureAwait(false);
			if (issue == null)
			{
				var newIssue = new NewIssue(issueTitle)
				{
					Body = issueBody.ToString()
				};
				issue = await context.GithubClient.Issue.Create(upstream.Owner.Login, upstream.Name, newIssue).ConfigureAwait(false);
				context.IssuesCreatedByCurrentUser.Add(issue);
			}
			else
			{
				var issueUpdate = new IssueUpdate()
				{
					Body = issueBody.ToString()
				};
				issue = await context.GithubClient.Issue.Update(upstream.Owner.Login, upstream.Name, issue.Number, issueUpdate).ConfigureAwait(false);
			}

			// Submit a PR when all addins have been upgraded to latest Cake
			var totalReferencesCount = recipeFilesWithAtLeastOneReference.Sum(recipeFile => recipeFile.AddinReferences.Count());
			var availableForLatestCakeVersionCount = recipeFilesWithAtLeastOneReference.Sum(recipeFile => recipeFile.AddinReferences.Count(r => !string.IsNullOrEmpty(r.LatestVersionForLatestCake)));

			if (availableForLatestCakeVersionCount == totalReferencesCount)
			{
				var packagesConfig = new StringBuilder();
				packagesConfig.AppendUnixLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
				packagesConfig.AppendUnixLine("<packages>");
				packagesConfig.AppendUnixLine($"    <package id=\"Cake\" version=\"{latestCakeVersion.Version.ToString(3)}\" />");
				packagesConfig.AppendUnixLine("</packages>");

				// Commit changes to a new branch and submit PR
				var pullRequestTitle = $"Upgrade to Cake {latestCakeVersion.Version.ToString(3)}";
				var newBranchName = $"upgrade_cake_{DateTime.UtcNow:yyyy_MM_dd_HH_mm_ss}";

				var pullRequest = await Misc.FindGithubPullRequestAsync(context, upstream.Owner.Login, upstream.Name, context.Options.GithubUsername, pullRequestTitle).ConfigureAwait(false);
				if (pullRequest == null)
				{
					var commits = new List<(string CommitMessage, IEnumerable<string> FilesToDelete, IEnumerable<(EncodingType Encoding, string Path, string Content)> FilesToUpsert)>
					{
						(CommitMessage: "Update addins references", FilesToDelete: null, FilesToUpsert: recipeFilesWithAtLeastOneReference.Select(recipeFile => (EncodingType: EncodingType.Utf8, recipeFile.Path, Content: recipeFile.GetContentForLatestCake())).ToArray()),
						(CommitMessage: "Update Cake version in package.config", FilesToDelete: null, FilesToUpsert: new[] { (EncodingType: EncodingType.Utf8, Path: Constants.PACKAGES_CONFIG_PATH, Content: packagesConfig.ToString()) })
					};

					pullRequest = await Misc.CommitToNewBranchAndSubmitPullRequestAsync(context, fork, issue.Number, newBranchName, pullRequestTitle, commits).ConfigureAwait(false);
					context.PullRequestsCreatedByCurrentUser.Add(pullRequest);
				}
			}
		}
	}
}
