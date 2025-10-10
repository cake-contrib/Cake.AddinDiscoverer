using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
using Newtonsoft.Json.Linq;
using NuGet.Common;
using NuGet.Protocol.Core.Types;
using Octokit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Cake.AddinDiscoverer.Models.ReportData;
using Constants = Cake.AddinDiscoverer.Utilities.Constants;

namespace Cake.AddinDiscoverer.Steps
{
	internal class UpdateRecipeStep : IStep
	{
		// A recipe must support Cake 0.38.5 (which was the last pre-1.0.0 version) at the very least, in order for AddinDiscoverer to be able to suggest upgrades
		private static readonly SemVersion _minSupportedCakeVersion = new SemVersion(0, 38, 5);

		// This is a bogus version that is intended to represent any version of Cake greather than nextCakeVersion
		// For example: if Cake 2.0 is the next version of Cake, this bogus version would represent Cake 3.0 and 4.0
		// and any subsequent release of Cake
		private static readonly CakeVersion _subsequentCakeVersion = new() { Version = new SemVersion(-1) };

		// This is a bogus version that is intended to represent any version of Cake less than currentCakeVersion
		// For example: if Cake 3.0 is the current version of Cake, this bogus version would represent Cake 1.0 and 2.0
		private static readonly CakeVersion _priorCakeVersion = new() { Version = new SemVersion(-2) };

		private readonly RecipeRepo _recipeRepo;

		public bool ContinueOnError { get { return true; } }

		public UpdateRecipeStep(RecipeRepo recipeRepo)
		{
			_recipeRepo = recipeRepo;
		}

		public bool PreConditionIsMet(DiscoveryContext context) => context.Options.UpdateRecipes;

		public string GetDescription(DiscoveryContext context) => $"Update {_recipeRepo.Owner}/{_recipeRepo.Name}";

		public async Task ExecuteAsync(DiscoveryContext context, TextWriter log, CancellationToken cancellationToken)
		{
			var cakeVersionUsedByRecipe = await FindCakeVersionUsedByRecipe(context, _recipeRepo).ConfigureAwait(false);
			if (cakeVersionUsedByRecipe < _minSupportedCakeVersion)
			{
				throw new Exception($"{_recipeRepo.Owner}/{_recipeRepo.Name} currently supports Cake {cakeVersionUsedByRecipe.ToString(3)}. It must support {_minSupportedCakeVersion.ToString(3)} at the very least in order for AddinDiscoverer to be able to make upgrade suggestions.");
			}

			await UpdateCakeRecipeAsync(context, _recipeRepo, cakeVersionUsedByRecipe).ConfigureAwait(false);
		}

		private static async Task<SemVersion> FindCakeVersionUsedByRecipe(DiscoveryContext context, RecipeRepo recipeRepo)
		{
			// Try to get Cake version from cake-version.yml
			var cakeVersion = await FindCakeVersionUsedByRecipeFromCakeVersionYaml(context, recipeRepo).ConfigureAwait(false);

			// Fallback on dotnet-tools.json
			cakeVersion ??= await FindCakeVersionUsedByRecipeFromDotNetConfig(context, recipeRepo).ConfigureAwait(false);

			return cakeVersion ?? throw new Exception($"Unable to detect the version of Cake used by {recipeRepo.Owner}/{recipeRepo.Name}");
		}

		private static async Task<SemVersion> FindCakeVersionUsedByRecipeFromCakeVersionYaml(DiscoveryContext context, RecipeRepo recipeRepo)
		{
			try
			{
				var contents = await context.GithubClient.Repository.Content.GetAllContents(recipeRepo.Owner, recipeRepo.Name, recipeRepo.VersionFilePath).ConfigureAwait(false);
				var deserializer = new YamlDotNet.Serialization.Deserializer();
				var yamlConfig = deserializer.Deserialize<CakeVersionYamlConfig>(contents[0].Content);
				return yamlConfig.TargetCakeVersion;
			}
			catch (NotFoundException)
			{
				return null;
			}
		}

		private static async Task<SemVersion> FindCakeVersionUsedByRecipeFromDotNetConfig(DiscoveryContext context, RecipeRepo recipeRepo)
		{
			try
			{
				var contents = await context.GithubClient.Repository.Content.GetAllContents(recipeRepo.Owner, recipeRepo.Name, Constants.DOT_NET_TOOLS_CONFIG_PATH).ConfigureAwait(false);
				var jObject = JObject.Parse(contents[0].Content);
				var versionNode = jObject["tools"]?["cake.tool"]?["version"];
				return versionNode == null ? null : SemVersion.Parse(versionNode.Value<string>());
			}
			catch (NotFoundException)
			{
				return null;
			}
		}

		private static async Task UpdateCakeRecipeAsync(DiscoveryContext context, RecipeRepo recipeRepo, SemVersion cakeVersionUsedInRecipe)
		{
			var currentCakeVersion = Constants.CAKE_VERSIONS.Where(v => v.Version <= cakeVersionUsedInRecipe).Max();
			var nextCakeVersion = Constants.CAKE_VERSIONS.Where(v => v.Version > cakeVersionUsedInRecipe).Min();

			// Get the ".cake" files
			var recipeFiles = await GetRecipeFilesAsync(context, recipeRepo, currentCakeVersion, nextCakeVersion).ConfigureAwait(false);

			// Submit a PR if any addin reference is outdated
			await UpdateOutdatedRecipeFilesAsync(context, recipeRepo, recipeFiles).ConfigureAwait(false);

			// Either submit a PR to upgrade to the next version of Cake OR create an issue explaining why Cake.Recipe cannot be upgraded to next Cake version
			if (nextCakeVersion != null && currentCakeVersion.Version < nextCakeVersion.Version)
			{
				await UpgradeCakeVersionUsedByRecipeAsync(context, recipeRepo, recipeFiles, nextCakeVersion).ConfigureAwait(false);
			}
		}

		private static async Task<RecipeFile[]> GetRecipeFilesAsync(DiscoveryContext context, RecipeRepo recipeRepo, CakeVersion currentCakeVersion, CakeVersion nextCakeVersion)
		{
			var directoryContent = await context.GithubClient.Repository.Content.GetAllContents(recipeRepo.Owner, recipeRepo.Name, recipeRepo.ContentFolderPath).ConfigureAwait(false);
			var cakeFiles = directoryContent.Where(c => c.Type == new StringEnum<ContentType>(ContentType.File) && c.Name.EndsWith(".cake", StringComparison.OrdinalIgnoreCase));

			var reportData = new ReportData(context.Addins);
			var addins = new Dictionary<CakeVersion, IEnumerable<AddinMetadata>>
			{
				{ _priorCakeVersion, reportData.GetAddinsForCakeVersion(currentCakeVersion, CakeVersionComparison.LessThan) },
				{ currentCakeVersion, reportData.GetAddinsForCakeVersion(currentCakeVersion, CakeVersionComparison.Equal) }
			};

			if (nextCakeVersion != null)
			{
				addins.Add(nextCakeVersion, reportData.GetAddinsForCakeVersion(nextCakeVersion, CakeVersionComparison.Equal));
				addins.Add(_subsequentCakeVersion, reportData.GetAddinsForCakeVersion(nextCakeVersion, CakeVersionComparison.GreaterThan));
			}

			// Local functions that indicates if an addin corresponds to the reference
			static bool AddinIsEqualToReference(AddinMetadata addin, AddinReference addinReference)
			{
				return addin.Name.EqualsIgnoreCase(addinReference.Name) && !addin.IsPrerelease;
			}

			var recipeFiles = await cakeFiles
				.ForEachAsync(
					async cakeFile =>
					{
						var contents = await context.GithubClient.Repository.Content.GetAllContents(recipeRepo.Owner, recipeRepo.Name, cakeFile.Path).ConfigureAwait(false);

						var recipeFile = new RecipeFile()
						{
							Name = cakeFile.Name,
							Path = cakeFile.Path,
							Content = contents[0].Content
						};

						foreach (var addinReference in recipeFile.AddinReferences)
						{
							addinReference.LatestVersionForAnyPreviousCake = addins[_priorCakeVersion]
								.SingleOrDefault(addin => AddinIsEqualToReference(addin, addinReference))?
								.NuGetPackageVersion
								.ToSemVersion();

							addinReference.LatestVersionForCurrentCake = addins[currentCakeVersion]
								.SingleOrDefault(addin => AddinIsEqualToReference(addin, addinReference))?
								.NuGetPackageVersion
								.ToSemVersion();

							if (nextCakeVersion != null)
							{
								addinReference.LatestVersionForNextCake = addins[nextCakeVersion]
									.SingleOrDefault(addin => AddinIsEqualToReference(addin, addinReference))?
									.NuGetPackageVersion
									.ToSemVersion();

								addinReference.LatestVersionForLatestCake = addins[_subsequentCakeVersion]
									.SingleOrDefault(addin => AddinIsEqualToReference(addin, addinReference))?
									.NuGetPackageVersion
									.ToSemVersion();
							}
						}

						foreach (var loadReference in recipeFile.LoadReferences)
						{
							var allVersions = reportData.AllPackages.Where(addin => AddinIsEqualToReference(addin, loadReference)).ToArray();

							var latestVersionForCurrentCake = addins[currentCakeVersion].SingleOrDefault(addin => AddinIsEqualToReference(addin, loadReference));
							latestVersionForCurrentCake ??= allVersions.FirstOrDefault(addin => addin.CakeVersionYaml == null || addin.CakeVersionYaml.TargetCakeVersion < currentCakeVersion.Version);

							var latestVersionForNextCake = addins[nextCakeVersion].SingleOrDefault(addin => AddinIsEqualToReference(addin, loadReference));
							latestVersionForNextCake ??= allVersions.FirstOrDefault(addin => addin.CakeVersionYaml == null || addin.CakeVersionYaml.TargetCakeVersion < nextCakeVersion.Version);

							var latestVersionForLatestCake = addins[_subsequentCakeVersion].SingleOrDefault(addin => AddinIsEqualToReference(addin, loadReference));
							latestVersionForLatestCake ??= allVersions.FirstOrDefault();

							loadReference.LatestVersionForCurrentCake = latestVersionForCurrentCake.NuGetPackageVersion.ToSemVersion();
							loadReference.LatestVersionForNextCake = latestVersionForNextCake.NuGetPackageVersion.ToSemVersion();
							loadReference.LatestVersionForLatestCake = latestVersionForLatestCake.NuGetPackageVersion.ToSemVersion();
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
							},
							Constants.MAX_NUGET_CONCURENCY)
						.ConfigureAwait(false);

						return recipeFile;
					},
					Constants.MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);

			return recipeFiles;
		}

		private static async Task UpdateOutdatedRecipeFilesAsync(DiscoveryContext context, RecipeRepo recipeRepo, RecipeFile[] recipeFiles)
		{
			// Make sure there is at least one outdated reference
			var outdatedReferences = recipeFiles
				.SelectMany(recipeFile => recipeFile.AddinReferences
					.Where(r => r.LatestVersionForCurrentCake != null && r.ReferencedVersion < r.LatestVersionForCurrentCake)
					.Select(r => new { Recipe = recipeFile, Reference = (CakeReference)r, Type = "addin", LatestVersion = r.LatestVersionForCurrentCake }))
				.Union(recipeFiles
					.SelectMany(recipeFile => recipeFile.ToolReferences
						.Where(r => r.LatestVersion != null && r.ReferencedVersion < r.LatestVersion)
						.Select(r => new { Recipe = recipeFile, Reference = (CakeReference)r, Type = "tool", r.LatestVersion })))
				.Concat(recipeFiles
					.SelectMany(recipeFile => recipeFile.LoadReferences
						.Where(r => r.LatestVersionForCurrentCake != null && r.ReferencedVersion < r.LatestVersionForCurrentCake)
					.Select(r => new { Recipe = recipeFile, Reference = (CakeReference)r, Type = "load", LatestVersion = r.LatestVersionForCurrentCake })))
				.ToArray();
			if (outdatedReferences.Length == 0) return;

			// Ensure the fork is up-to-date
			var fork = await context.GithubClient.CreateOrRefreshFork(recipeRepo.Owner, recipeRepo.Name).ConfigureAwait(false);
			var upstream = fork.Parent;

			if (context.Options.DryRun)
			{
				var commits = new List<(string CommitMessage, IEnumerable<string> FilesToDelete, IEnumerable<(EncodingType Encoding, string Path, string Content)> FilesToUpsert)>();

				foreach (var grp in outdatedReferences.GroupBy(r => r.Recipe))
				{
					// Create a commit with all the changes to the same recipe file
					var commitMessageLong = $"Update {grp.Count()} reference(s) in {grp.Key.Name}";
					var content = grp.Key.GetContentForCurrentCake(grp.Select(r => r.Reference));
					commits.Add((commitMessageLong, null, new[] { (EncodingType: EncodingType.Utf8, grp.Key.Path, Content: content) }));
				}

				var commitMessageShort = "Update outdated reference";
				var newBranchName = $"dryrun_update_{recipeRepo.Name.ToLower().Replace('.', '_')}_references_{DateTime.UtcNow:yyyy_MM_dd_HH_mm_ss}";
				await Misc.CommitToNewBranchAndSubmitPullRequestAsync(context, fork, null, newBranchName, commitMessageShort, commits).ConfigureAwait(false);
			}
			else
			{
				var updatedReferencesCount = 0;

				// Create an issue and PR for each outdated reference
				foreach (var outdatedReference in outdatedReferences)
				{
					// Limit the number of outdated references we will update in this run in an attempt to
					// reduce the number of commits and therefore avoid triggering GitHub's abuse detection.
					// The remaining outdated references will be updated in subsequent run(s).
					if (updatedReferencesCount < 5)
					{
						// Check if an issue already exists
						var issueTitle = $"Reference to {outdatedReference.Type} {outdatedReference.Reference.Name} in {outdatedReference.Recipe.Name} needs to be updated";
						var issue = await Misc.FindGithubIssueAsync(context, upstream.Owner.Login, upstream.Name, context.Options.GithubUsername, issueTitle).ConfigureAwait(false);
						if (issue != null) continue;

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

						await Misc.CommitToNewBranchAndSubmitPullRequestAsync(context, fork, issue?.Number, newBranchName, commitMessageShort, commits).ConfigureAwait(false);
						await Misc.RandomGithubDelayAsync().ConfigureAwait(false);
						updatedReferencesCount++;
					}
				}
			}
		}

		private static async Task UpgradeCakeVersionUsedByRecipeAsync(DiscoveryContext context, RecipeRepo recipeRepo, RecipeFile[] recipeFiles, CakeVersion nextCakeVersion)
		{
			var recipeFilesWithAtLeastOneReference = recipeFiles
				.Where(recipeFile => recipeFile.AddinReferences.Any() || recipeFile.LoadReferences.Any())
				.ToArray();
			if (recipeFilesWithAtLeastOneReference.Length == 0) return;

			// Ensure the fork is up-to-date
			var fork = await context.GithubClient.CreateOrRefreshFork(recipeRepo.Owner, recipeRepo.Name).ConfigureAwait(false);
			var upstream = fork.Parent;

			// The content of the issue body
			var issueBody = new StringBuilder();
			issueBody.AppendFormat("In order for {0} to be compatible with Cake version {1}, each and every referenced addin must support Cake {1}. ", recipeRepo.Name, nextCakeVersion.Version.ToString(3));
			issueBody.AppendFormat("This issue will be used to track the full list of addins referenced in Cake.Recipe and whether or not they support Cake {0}. ", nextCakeVersion.Version.ToString(3));
			issueBody.AppendFormat("When all referenced addins are upgraded to support Cake {0}, we will automatically submit a PR to upgrade {1}. ", nextCakeVersion.Version.ToString(3), recipeRepo.Name);
			issueBody.AppendFormat("In the mean time, this issue will be regularly updated when addins are updated with Cake {0} support.{1}", nextCakeVersion.Version.ToString(3), Environment.NewLine);
			issueBody.AppendLine();
			issueBody.AppendLine("Referenced Addins:");
			foreach (var recipeFile in recipeFilesWithAtLeastOneReference)
			{
				if (recipeFile.AddinReferences.Any())
				{
					issueBody.AppendLine();
					issueBody.AppendLine($"- `{recipeFile.Name}` references the following addins:");
					foreach (var addinReference in recipeFile.AddinReferences)
					{
						if (addinReference.SkippedNextCake)
						{
							issueBody.AppendLine($"    - [x] {addinReference.Name} skipped support for Cake {nextCakeVersion.Version}. Therefore this reference will not be upgraded.");
						}
						else
						{
							issueBody.AppendLine($"    - [{(addinReference.UpdatedForNextCake ? "x" : " ")}] {addinReference.Name}");
						}
					}
				}

				if (recipeFile.LoadReferences.Any())
				{
					issueBody.AppendLine();
					issueBody.AppendLine($"- `{recipeFile.Name}` references the following recipes:");
					foreach (var loadReference in recipeFile.LoadReferences)
					{
						issueBody.AppendLine($"    - [{(loadReference.UpdatedForNextCake ? "x" : " ")}] {loadReference.Name}");
					}
				}
			}

			// Create a new issue or update existing one
			var issueTitle = string.Format(Constants.CAKE_RECIPE_UPGRADE_CAKE_VERSION_ISSUE_TITLE, nextCakeVersion.Version.ToString(3));
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
				var issueUpdate = issue.ToUpdate();
				issueUpdate.Body = issueBody.ToString();
				issue = await context.GithubClient.Issue.Update(upstream.Owner.Login, upstream.Name, issue.Number, issueUpdate).ConfigureAwait(false);
			}

			// Submit a PR when all addins have been upgraded to next version of Cake
			var totalReferencesCount = recipeFilesWithAtLeastOneReference.Sum(recipeFile =>
			{
				var addinReferences = recipeFile.AddinReferences.Count();
				var recipeReferences = recipeFile.LoadReferences.Count();
				return addinReferences + recipeReferences;
			});
			var availableForNextCakeVersionCount = recipeFilesWithAtLeastOneReference.Sum(recipeFile =>
			{
				var addinReferences = recipeFile.AddinReferences.Count(r => r.UpdatedForNextCake || r.SkippedNextCake);
				var recipeReferences = recipeFile.LoadReferences.Count(r => r.UpdatedForNextCake || r.SkippedNextCake);
				return addinReferences + recipeReferences;
			});

			if (availableForNextCakeVersionCount == totalReferencesCount)
			{
				var yamlVersionConfigContents = await context.GithubClient.Repository.Content.GetAllContents(recipeRepo.Owner, recipeRepo.Name, recipeRepo.VersionFilePath).ConfigureAwait(false);
				var yamlConfig = yamlVersionConfigContents[0].Content.FromYamlString<CakeVersionYamlConfig>();
				yamlConfig.TargetCakeVersion = nextCakeVersion.Version;

				// Commit changes to a new branch and submit PR
				var pullRequestTitle = $"Upgrade to Cake {nextCakeVersion.Version.ToString(3)}";
				var newBranchName = $"upgrade_cake_{DateTime.UtcNow:yyyy_MM_dd_HH_mm_ss}";

				var pullRequest = await Misc.FindGithubPullRequestAsync(context, upstream.Owner.Login, upstream.Name, context.Options.GithubUsername, pullRequestTitle).ConfigureAwait(false);
				if (pullRequest == null)
				{
					var commits = new List<(string CommitMessage, IEnumerable<string> FilesToDelete, IEnumerable<(EncodingType Encoding, string Path, string Content)> FilesToUpsert)>
					{
						(CommitMessage: "Update addins references", FilesToDelete: null, FilesToUpsert: recipeFilesWithAtLeastOneReference.Select(recipeFile => (EncodingType: EncodingType.Utf8, recipeFile.Path, Content: recipeFile.GetContentForNextCake())).ToArray()),
						(CommitMessage: "Update Cake version in cake-version.yml", FilesToDelete: null, FilesToUpsert: new[] { (EncodingType: EncodingType.Utf8, Path: recipeRepo.VersionFilePath, Content: yamlConfig.ToYamlString()) })
					};

					pullRequest = await Misc.CommitToNewBranchAndSubmitPullRequestAsync(context, fork, issue?.Number, newBranchName, pullRequestTitle, commits).ConfigureAwait(false);
					if (pullRequest != null) context.PullRequestsCreatedByCurrentUser.Add(pullRequest);
				}
			}
		}
	}
}
