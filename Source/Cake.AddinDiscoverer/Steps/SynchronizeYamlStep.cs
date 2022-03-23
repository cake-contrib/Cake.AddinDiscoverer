using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using Octokit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using YamlDotNet.RepresentationModel;

namespace Cake.AddinDiscoverer.Steps
{
	internal class SynchronizeYamlStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => context.Options.SynchronizeYaml;

		public string GetDescription(DiscoveryContext context) => "Synchronize yml files on the Cake web site";

		public async Task ExecuteAsync(DiscoveryContext context, TextWriter log, CancellationToken cancellationToken)
		{
			// Arbitrary max number of files to delete, add and modify in a given commit.
			// This is to avoid AbuseException when commiting too many files.
			const int MAX_FILES_TO_COMMIT = 75;

			// Ensure the fork is up-to-date
			var fork = await context.GithubClient.CreateOrRefreshFork(Constants.CAKE_REPO_OWNER, Constants.CAKE_WEBSITE_REPO_NAME).ConfigureAwait(false);

			// Local functions that indicate if the YAML file for a given addin should be created/updated/deleted
			bool ShouldDeleteYamlFile(AddinMetadata addin)
			{
				return addin.IsDeprecated;
			}

			bool ShouldUpdateOrCreateYamlFile(AddinMetadata addin)
			{
				return !addin.IsDeprecated &&
					   (addin.Type == AddinType.Addin || addin.Type == AddinType.Module);
			}

			// --------------------------------------------------
			// Discover if any files need to be added/deleted/modified
			var directoryContent = await context.GithubClient.Repository.Content.GetAllContents(Constants.CAKE_REPO_OWNER, Constants.CAKE_WEBSITE_REPO_NAME, "extensions").ConfigureAwait(false);
			var yamlFiles = directoryContent
				.Where(file => file.Name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
				.Where(file => string.IsNullOrEmpty(context.Options.AddinName) || Path.GetFileNameWithoutExtension(file.Name) == context.Options.AddinName)
				.ToArray();

			var yamlFilesToBeDeleted = yamlFiles
				.Where(f =>
				{
					var addin = context.Addins.FirstOrDefault(a => a.Name == Path.GetFileNameWithoutExtension(f.Name));
					return addin == null || ShouldDeleteYamlFile(addin);
				})
				.OrderBy(f => f.Name)
				.Take(MAX_FILES_TO_COMMIT)
				.ToArray();

			var addinsWithContent = await context.Addins
				.Where(ShouldUpdateOrCreateYamlFile)
				.Where(addin => yamlFiles.Any(f => Path.GetFileNameWithoutExtension(f.Name) == addin.Name))
				.ForEachAsync<AddinMetadata, (AddinMetadata Addin, string CurrentContent, string NewContent)>(
					async addin =>
					{
						var contents = await context.GithubClient.Repository.Content.GetAllContents(Constants.CAKE_REPO_OWNER, Constants.CAKE_WEBSITE_REPO_NAME, $"extensions/{addin.Name}.yml").ConfigureAwait(false);
						return (addin, contents[0].Content, UpdateYamlFile(context, addin, contents[0].Content));
					}, Constants.MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);

			var addinsToBeUpdated = addinsWithContent
				.Where(addin => addin.CurrentContent != addin.NewContent)
				.OrderBy(addin => addin.Addin.Name)
				.Take(MAX_FILES_TO_COMMIT)
				.ToArray();

			var addinsToBeCreated = context.Addins
				.Where(ShouldUpdateOrCreateYamlFile)
				.Where(addin => !yamlFiles.Any(f => Path.GetFileNameWithoutExtension(f.Name) == addin.Name))
				.OrderBy(addin => addin.Name)
				.Select<AddinMetadata, (AddinMetadata Addin, string CurrentContent, string NewContent)>(addin => (addin, string.Empty, GenerateYamlFile(context, addin)))
				.Where(addin => !string.IsNullOrEmpty(addin.NewContent))
				.Take(MAX_FILES_TO_COMMIT)
				.ToArray();

			if (context.Options.DryRun)
			{
				// All changes created in a single branch and we don't create issues + PRs
				await DryRunAsync(context, fork, yamlFilesToBeDeleted, addinsToBeCreated, addinsToBeUpdated).ConfigureAwait(false);
			}
			else
			{
				// Check if an issue already exists
				var upstream = fork.Parent;
				var issue = await Misc.FindGithubIssueAsync(context, upstream.Owner.Login, upstream.Name, context.Options.GithubUsername, Constants.COLLECTIVE_YAML_SYNCHRONIZATION_ISSUE_TITLE).ConfigureAwait(false);

				if (issue != null)
				{
					return;
				}
				else if (yamlFilesToBeDeleted.Length + addinsToBeCreated.Length + addinsToBeUpdated.Length > 25)
				{
					// Changes are committed to a single branch, one single issue is raised and a single PRs is opened
					await SynchronizeYamlFilesCollectivelyAsync(context, fork, yamlFilesToBeDeleted, addinsToBeCreated, addinsToBeUpdated).ConfigureAwait(false);
				}
				else
				{
					// Each change is committed in a separate branch with their own issue and PR
					await SynchronizeYamlFilesIndividuallyAsync(context, fork, yamlFilesToBeDeleted, addinsToBeCreated, addinsToBeUpdated).ConfigureAwait(false);
				}
			}
		}

		private static async Task DryRunAsync(DiscoveryContext context, Repository fork, RepositoryContent[] yamlFilesToBeDeleted, (AddinMetadata Addin, string CurrentContent, string NewContent)[] addinsToBeCreated, (AddinMetadata Addin, string CurrentContent, string NewContent)[] addinsToBeUpdated)
		{
			var newBranchName = $"dryrun_{DateTime.UtcNow:yyyy_MM_dd_HH_mm_ss}";
			var commits = ConvertToCommits(yamlFilesToBeDeleted, addinsToBeCreated, addinsToBeUpdated);

			if (commits.Any())
			{
				var apiInfo = context.GithubClient.GetLastApiInfo();
				var requestsLeft = apiInfo?.RateLimit?.Remaining ?? 0;

				if (requestsLeft < Constants.MIN_GITHUB_REQUESTS_THRESHOLD)
				{
					Console.WriteLine($"DRY RUN - Only {requestsLeft} GitHub API requests left. Therefore skipping Dry run");
				}
				else
				{
					await Misc.CommitToNewBranchAsync(context, fork, null, newBranchName, commits).ConfigureAwait(false);

					const string githubUrl = "https://github.com";
					Console.WriteLine($"DRY RUN - view diff here: {githubUrl}/{fork.Parent.Owner.Login}/{fork.Parent.Name}/compare/{fork.Parent.DefaultBranch}...{fork.Owner.Login}:{newBranchName}");
				}
			}
			else
			{
				Console.WriteLine("DRY RUN - no YAML files to delete, create or modify");
			}
		}

		private static async Task SynchronizeYamlFilesIndividuallyAsync(DiscoveryContext context, Repository fork, RepositoryContent[] yamlFilesToBeDeleted, (AddinMetadata Addin, string CurrentContent, string NewContent)[] addinsToBeCreated, (AddinMetadata Addin, string CurrentContent, string NewContent)[] addinsToBeUpdated)
		{
			var upstream = fork.Parent;

			if (yamlFilesToBeDeleted.Any())
			{
				foreach (var yamlFileToBeDeleted in yamlFilesToBeDeleted)
				{
					var issueTitle = $"Delete {yamlFileToBeDeleted.Name}";

					// Check if an issue already exists
					var issue = await Misc.FindGithubIssueAsync(context, upstream.Owner.Login, upstream.Name, context.Options.GithubUsername, issueTitle).ConfigureAwait(false);
					if (issue == null)
					{
						var apiInfo = context.GithubClient.GetLastApiInfo();
						var requestsLeft = apiInfo?.RateLimit?.Remaining ?? 0;

						if (requestsLeft < Constants.MIN_GITHUB_REQUESTS_THRESHOLD)
						{
							Console.WriteLine($"  Only {requestsLeft} GitHub API requests left. Therefore skipping PR to delete {yamlFileToBeDeleted.Name}.yaml");
							continue;
						}

						// Create issue
						var newIssue = new NewIssue(issueTitle)
						{
							Body = $"The Cake.AddinDiscoverer tool has discovered that a YAML file exists on the Cake web site for a nuget package that is no longer available on NuGet.org.{Environment.NewLine}" +
								$"{Environment.NewLine}{yamlFileToBeDeleted.Name}.yaml must be deleted.{Environment.NewLine}"
						};
						issue = await context.GithubClient.Issue.Create(Constants.CAKE_REPO_OWNER, Constants.CAKE_WEBSITE_REPO_NAME, newIssue).ConfigureAwait(false);
						context.IssuesCreatedByCurrentUser.Add(issue);

						// Commit changes to a new branch and submit PR
						var newBranchName = $"delete_{yamlFileToBeDeleted.Name}_{DateTime.UtcNow:yyyy_MM_dd_HH_mm_ss}";
						var commits = new List<(string CommitMessage, IEnumerable<string> FilesToDelete, IEnumerable<(EncodingType Encoding, string Path, string Content)> FilesToUpsert)>
							{
								(CommitMessage: issueTitle, FilesToDelete: new[] { yamlFileToBeDeleted.Path }, FilesToUpsert: null)
							};

						var pullRequest = await Misc.CommitToNewBranchAndSubmitPullRequestAsync(context, fork, issue?.Number, newBranchName, issueTitle, commits).ConfigureAwait(false);
						if (pullRequest != null) context.PullRequestsCreatedByCurrentUser.Add(pullRequest);

						// This delay is important to avoid triggering GitHub's abuse protection
						await Misc.RandomGithubDelayAsync().ConfigureAwait(false);
					}
				}
			}

			if (addinsToBeCreated.Any())
			{
				foreach (var addinToBeCreated in addinsToBeCreated)
				{
					var issueTitle = $"Add {addinToBeCreated.Addin.Name}.yml";

					// Check if an issue already exists
					var issue = await Misc.FindGithubIssueAsync(context, upstream.Owner.Login, upstream.Name, context.Options.GithubUsername, issueTitle).ConfigureAwait(false);
					if (issue == null)
					{
						var apiInfo = context.GithubClient.GetLastApiInfo();
						var requestsLeft = apiInfo?.RateLimit?.Remaining ?? 0;

						if (requestsLeft < Constants.MIN_GITHUB_REQUESTS_THRESHOLD)
						{
							Console.WriteLine($"  Only {requestsLeft} GitHub API requests left. Therefore skipping PR to Create {addinToBeCreated.Addin.Name}.yaml");
							continue;
						}

						// Create issue
						var newIssue = new NewIssue(issueTitle)
						{
							Body = $"The Cake.AddinDiscoverer tool has discovered a NuGet package for a Cake addin without a corresponding yaml file on the Cake web site.{Environment.NewLine}" +
									$"{Environment.NewLine}{addinToBeCreated.Addin.Name}.yaml must be created.{Environment.NewLine}"
						};
						issue = await context.GithubClient.Issue.Create(Constants.CAKE_REPO_OWNER, Constants.CAKE_WEBSITE_REPO_NAME, newIssue).ConfigureAwait(false);
						context.IssuesCreatedByCurrentUser.Add(issue);

						// Commit changes to a new branch and submit PR
						var newBranchName = $"add_{addinToBeCreated.Addin.Name}.yml_{DateTime.UtcNow:yyyy_MM_dd_HH_mm_ss}";
						var commits = new List<(string CommitMessage, IEnumerable<string> FilesToDelete, IEnumerable<(EncodingType Encoding, string Path, string Content)> FilesToUpsert)>
						{
							(CommitMessage: issueTitle, FilesToDelete: null, FilesToUpsert: new[] { (Encoding: EncodingType.Utf8, Path: $"extensions/{addinToBeCreated.Addin.Name}.yml", Content: addinToBeCreated.NewContent) })
						};

						var pullRequest = await Misc.CommitToNewBranchAndSubmitPullRequestAsync(context, fork, issue?.Number, newBranchName, issueTitle, commits).ConfigureAwait(false);
						if (pullRequest != null) context.PullRequestsCreatedByCurrentUser.Add(pullRequest);

						// This is minimize the likelihood of triggering Github's abuse detection
						await Misc.RandomGithubDelayAsync().ConfigureAwait(false);
					}
				}
			}

			if (addinsToBeUpdated.Any())
			{
				foreach (var addinToBeUpdated in addinsToBeUpdated)
				{
					var issueTitle = $"Update {addinToBeUpdated.Addin.Name}.yml";

					// Check if an issue already exists
					var issue = await Misc.FindGithubIssueAsync(context, upstream.Owner.Login, upstream.Name, context.Options.GithubUsername, issueTitle).ConfigureAwait(false);
					if (issue == null)
					{
						var apiInfo = context.GithubClient.GetLastApiInfo();
						var requestsLeft = apiInfo?.RateLimit?.Remaining ?? 0;

						if (requestsLeft < Constants.MIN_GITHUB_REQUESTS_THRESHOLD)
						{
							Console.WriteLine($"  Only {requestsLeft} GitHub API requests left. Therefore skipping PR to update {addinToBeUpdated.Addin.Name}.yaml");
							continue;
						}

						// Create issue
						var newIssue = new NewIssue(issueTitle)
						{
							Body = $"The Cake.AddinDiscoverer tool has discovered discrepancies between {addinToBeUpdated.Addin.Name}.yaml on Cake's web site and the metadata in the packages discovered on NuGet.org.{Environment.NewLine}" +
									$"{Environment.NewLine}{addinToBeUpdated.Addin.Name}.yaml must be updated.{Environment.NewLine}"
						};
						issue = await context.GithubClient.Issue.Create(Constants.CAKE_REPO_OWNER, Constants.CAKE_WEBSITE_REPO_NAME, newIssue).ConfigureAwait(false);
						context.IssuesCreatedByCurrentUser.Add(issue);

						// Prepare changes
						var newBranchName = $"update_{addinToBeUpdated.Addin.Name}.yml_{DateTime.UtcNow:yyyy_MM_dd_HH_mm_ss}";
						var commits =
							new List<(string CommitMessage, IEnumerable<string> FilesToDelete, IEnumerable<(EncodingType Encoding, string Path, string Content)> FilesToUpsert)>
							{
								(CommitMessage: issueTitle, FilesToDelete: null,
									FilesToUpsert: new[]
									{
										(Encoding: EncodingType.Utf8, Path: $"extensions/{addinToBeUpdated.Addin.Name}.yml", Content: addinToBeUpdated.NewContent)
									})
							};

						var pullRequest = await Misc.CommitToNewBranchAndSubmitPullRequestAsync(context, fork, issue?.Number, newBranchName, issueTitle, commits).ConfigureAwait(false);
						if (pullRequest != null) context.PullRequestsCreatedByCurrentUser.Add(pullRequest);

						// This is minimize the likelihood of triggering Github's abuse detection
						await Misc.RandomGithubDelayAsync().ConfigureAwait(false);
					}
				}
			}
		}

		private static async Task SynchronizeYamlFilesCollectivelyAsync(DiscoveryContext context, Repository fork, RepositoryContent[] yamlFilesToBeDeleted, (AddinMetadata Addin, string CurrentContent, string NewContent)[] addinsToBeCreated, (AddinMetadata Addin, string CurrentContent, string NewContent)[] addinsToBeUpdated)
		{
			// Filter out YAML files that already have an open issue+PR
			var apiInfo = context.GithubClient.GetLastApiInfo();
			var requestsLeft = apiInfo?.RateLimit?.Remaining ?? 0;

			var threshold = Math.Max(Constants.MIN_GITHUB_REQUESTS_THRESHOLD, (yamlFilesToBeDeleted.Length + addinsToBeCreated.Length + addinsToBeUpdated.Length) * 3);
			if (requestsLeft < threshold)
			{
				Console.WriteLine($"  Only {requestsLeft} GitHub API requests left. Therefore skipping YAML files synchronization.");
				return;
			}

			var upstream = fork.Parent;

			// Filter out YAML files to be deleted if they already have an open issue
			var yamlFilesToBeDeletedWithIssue = await yamlFilesToBeDeleted
				.ForEachAsync(
					async yamlFileToBeDeleted =>
					{
						var issueTitle = $"Delete {yamlFileToBeDeleted.Name}";
						var issue = await Misc.FindGithubIssueAsync(context, upstream.Owner.Login, upstream.Name, context.Options.GithubUsername, string.Format(issueTitle, yamlFileToBeDeleted.Name)).ConfigureAwait(false);
						return (YamlFile: yamlFileToBeDeleted, Issue: issue);
					}, Constants.MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);

			var filteredYamlFilesToBeDeleted = yamlFilesToBeDeletedWithIssue
				.Where(fileWithIssue => fileWithIssue.Issue == null)
				.Select(fileWithIssue => fileWithIssue.YamlFile)
				.ToArray();

			// Filter out addins to be created if they already have an open issue
			var addinsToBeCreatedWithIssue = await addinsToBeCreated
				.ForEachAsync(
					async addinToBeCreated =>
					{
						var issueTitle = $"Add {addinToBeCreated.Addin.Name}.yml";
						var issue = await Misc.FindGithubIssueAsync(context, upstream.Owner.Login, upstream.Name, context.Options.GithubUsername, string.Format(issueTitle, addinToBeCreated.Addin.Name)).ConfigureAwait(false);
						return (Addin: addinToBeCreated, Issue: issue);
					}, Constants.MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);

			var filteredAddinsToBeCreated = addinsToBeCreatedWithIssue
				.Where(addinWithIssue => addinWithIssue.Issue == null)
				.Select(addinWithIssue => addinWithIssue.Addin)
				.ToArray();

			// Filter out addins to be updated if they already have an open issue
			var addinsToBeUpdatedWithIssue = await addinsToBeUpdated
				.ForEachAsync(
					async addinToBeUpdated =>
					{
						var issueTitle = $"Update {addinToBeUpdated.Addin.Name}.yml";
						var issue = await Misc.FindGithubIssueAsync(context, upstream.Owner.Login, upstream.Name, context.Options.GithubUsername, string.Format(issueTitle, addinToBeUpdated.Addin.Name)).ConfigureAwait(false);
						return (Addin: addinToBeUpdated, Issue: issue);
					}, Constants.MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);

			var filteredAddinsToBeUpdated = addinsToBeUpdatedWithIssue
				.Where(addinWithIssue => addinWithIssue.Issue == null)
				.Select(addinWithIssue => addinWithIssue.Addin)
				.ToArray();

			// Convert YAML files into commits
			var newBranchName = $"yaml_files_sync_{DateTime.UtcNow:yyyy_MM_dd_HH_mm_ss}";
			var commits = ConvertToCommits(filteredYamlFilesToBeDeleted, filteredAddinsToBeCreated, filteredAddinsToBeUpdated);

			if (commits.Any())
			{
				// Create issue
				var newIssue = new NewIssue(Constants.COLLECTIVE_YAML_SYNCHRONIZATION_ISSUE_TITLE)
				{
					Body = $"The Cake.AddinDiscoverer tool has discovered that a large number of YAML file need to be deleted, added or modified.{Environment.NewLine}" +
						   $"{Environment.NewLine}Since the number of files is larger than usual, we grouped them all together and we are raising a single issue and opening a single PR.{Environment.NewLine}"
				};
				var issue = await context.GithubClient.Issue.Create(Constants.CAKE_REPO_OWNER, Constants.CAKE_WEBSITE_REPO_NAME, newIssue).ConfigureAwait(false);
				context.IssuesCreatedByCurrentUser.Add(issue);

				// Commit changes to a new branch and submit PR
				var pullRequest = await Misc.CommitToNewBranchAndSubmitPullRequestAsync(context, fork, issue?.Number, newBranchName, Constants.COLLECTIVE_YAML_SYNCHRONIZATION_ISSUE_TITLE, commits).ConfigureAwait(false);
				if (pullRequest != null) context.PullRequestsCreatedByCurrentUser.Add(pullRequest);
			}
		}

		private static IEnumerable<(string CommitMessage, IEnumerable<string> FilesToDelete, IEnumerable<(EncodingType Encoding, string Path, string Content)> FilesToUpsert)> ConvertToCommits(RepositoryContent[] yamlFilesToBeDeleted, (AddinMetadata Addin, string CurrentContent, string NewContent)[] addinsToBeCreated, (AddinMetadata Addin, string CurrentContent, string NewContent)[] addinsToBeUpdated)
		{
#pragma warning disable SA1111 // Closing parenthesis should be on line of last parameter
#pragma warning disable SA1009 // Closing parenthesis should be spaced correctly
			if (yamlFilesToBeDeleted.Any())
			{
				yield return
				(
					CommitMessage: $"Delete {yamlFilesToBeDeleted.Length} YAML files",
					FilesToDelete: yamlFilesToBeDeleted.Select(yamlFile => yamlFile.Path).ToArray(),
					FilesToUpsert: null
				);
			}

			if (addinsToBeCreated.Any())
			{
				yield return
				(
					CommitMessage: $"Create {addinsToBeCreated.Length} YAML files",
					FilesToDelete: null,
					FilesToUpsert: addinsToBeCreated
						.Select<(AddinMetadata Addin, string CurrentContent, string NewContent), (EncodingType Encoding,
							string Path, string Content)>(addinToBeCreated =>
							(
								Encoding: EncodingType.Utf8,
								Path: $"extensions/{addinToBeCreated.Addin.Name}.yml",
								Content: addinToBeCreated.NewContent
							)
						)
				);
			}

			if (addinsToBeUpdated.Any())
			{
				yield return
				(
					CommitMessage: $"Update {addinsToBeUpdated.Length} YAML files",
					FilesToDelete: null,
					FilesToUpsert: addinsToBeUpdated
						.Select<(AddinMetadata Addin, string CurrentContent, string NewContent), (EncodingType Encoding,
							string Path, string Content)>(addinToBeUpdated =>
							(
								Encoding: EncodingType.Utf8,
								Path: $"extensions/{addinToBeUpdated.Addin.Name}.yml",
								Content: addinToBeUpdated.NewContent
							)
						)
				);
			}
#pragma warning restore SA1009 // Closing parenthesis should be spaced correctly
#pragma warning restore SA1111 // Closing parenthesis should be on line of last parameter
		}

		private static string GenerateYamlFile(DiscoveryContext context, AddinMetadata addin)
		{
			var description = string.IsNullOrEmpty(addin.Description) ? Constants.NO_DESCRIPTION_PROVIDED : addin.Description;

			var obj = new
			{
				Type = addin.Type,
				Name = addin.Name,
				NuGet = addin.Name,
				Assemblies = GetAssembliesForYaml(context, new[] { $"/**/{(string.IsNullOrEmpty(addin.DllName) ? "*.dll" : addin.DllName)}" }),
				Repository = (addin.InferredRepositoryUrl ?? addin.RepositoryUrl)?.AbsoluteUri,
				ProjectUrl = (addin.ProjectUrl ?? addin.NuGetPackageUrl)?.AbsoluteUri,
				Author = addin.GetMaintainerName(),
				Description = description,
				Categories = GetCategoriesForYaml(context, addin.Tags),
				TargetCakeVersion = addin.AnalysisResult.GetTargetedCakeVersion()?.ToString(),
				TargetFrameworks = GetFrameworksForYaml(addin.Frameworks),
				AnalyzedPackageVersion = addin.NuGetPackageVersion,
				AnalyzedPackageIsPrerelease = addin.IsPrerelease ? "true" : "false",
				AnalyzedPackagePublishDate = addin.PublishedOn == Constants.UtcMinDateTime ? string.Empty : addin.PublishedOn.UtcDateTime.ToString("o") // 'o' is the ISO 8601 format (see: https://docs.microsoft.com/en-us/dotnet/standard/base-types/standard-date-and-time-format-strings#Roundtrip)
			};

			var updatedYamlContent = obj.ToYamlString("\n");
			return updatedYamlContent;
		}

		private static string UpdateYamlFile(DiscoveryContext context, AddinMetadata addin, string currentYamlContent)
		{
			var input = new StringReader(currentYamlContent);
			var yaml = new YamlStream();
			yaml.Load(input);

			var mapping = (YamlMappingNode)yaml.Documents[0].RootNode;

			var obj = new
			{
				Type = addin.Type,
				Name = mapping.GetChildNodeValue("Name"),
				NuGet = mapping.GetChildNodeValue("NuGet"),
				Assemblies = GetAssembliesForYaml(context, mapping),
				Repository = (addin.InferredRepositoryUrl ?? addin.RepositoryUrl)?.AbsoluteUri,
				ProjectUrl = (addin.ProjectUrl ?? addin.NuGetPackageUrl)?.AbsoluteUri,
				Author = mapping.GetChildNodeValue("Author"),
				Description = mapping.GetChildNodeValue("Description"),
				Categories = GetCategoriesForYaml(context, mapping),
				TargetCakeVersion = addin.AnalysisResult.GetTargetedCakeVersion()?.ToString(),
				TargetFrameworks = GetFrameworksForYaml(addin.Frameworks),
				AnalyzedPackageVersion = addin.NuGetPackageVersion,
				AnalyzedPackageIsPrerelease = addin.IsPrerelease ? "true" : "false",
				AnalyzedPackagePublishDate = addin.PublishedOn == Constants.UtcMinDateTime ? string.Empty : addin.PublishedOn.UtcDateTime.ToString("o") // 'o' is the ISO 8601 format (see: https://docs.microsoft.com/en-us/dotnet/standard/base-types/standard-date-and-time-format-strings#Roundtrip)
			};

			var updatedYamlContent = obj.ToYamlString("\n");
			return updatedYamlContent;
		}

		private static IEnumerable<string> GetAssembliesForYaml(DiscoveryContext context, YamlMappingNode mapping)
		{
			var key = new YamlScalarNode("Assemblies");
			if (!mapping.Children.ContainsKey(key)) return Enumerable.Empty<string>();

			var paths = mapping.Children[key] switch
			{
				YamlScalarNode scalarNode => new[] { scalarNode.ToString() },
				YamlSequenceNode sequenceNode => sequenceNode.Select(node => node.ToString()),
				_ => Enumerable.Empty<string>()
			};

			return GetAssembliesForYaml(context, paths);
		}

		private static IEnumerable<string> GetAssembliesForYaml(DiscoveryContext context, IEnumerable<string> paths)
		{
			var filteredPaths = paths
				.Where(t1 => !string.IsNullOrWhiteSpace(t1))
				.Select(t2 => t2.Trim())
				.Distinct(StringComparer.InvariantCultureIgnoreCase);

			return filteredPaths;
		}

		private static IEnumerable<string> GetCategoriesForYaml(DiscoveryContext context, YamlMappingNode mapping)
		{
			var key = new YamlScalarNode("Categories");
			if (!mapping.Children.ContainsKey(key)) return Enumerable.Empty<string>();

			var tags = mapping.Children[key] switch
			{
				YamlScalarNode scalarNode => new[] { scalarNode.ToString() },
				YamlSequenceNode sequenceNode => sequenceNode.Select(node => node.ToString()),
				_ => Enumerable.Empty<string>()
			};

			return GetCategoriesForYaml(context, tags);
		}

		private static IEnumerable<string> GetCategoriesForYaml(DiscoveryContext context, IEnumerable<string> tags)
		{
			var filteredTags = tags
				.Where(t1 => !string.IsNullOrWhiteSpace(t1))
				.Select(t2 => t2.Trim())
				.Select(t3 => t3.ToLowerInvariant())
				.Except(context.ExcludedTags, StringComparer.InvariantCultureIgnoreCase)
				.Distinct(StringComparer.InvariantCultureIgnoreCase);

			return filteredTags;
		}

		private static IEnumerable<string> GetFrameworksForYaml(IEnumerable<string> frameworks)
		{
			var filteredFrameworks = frameworks
				.Where(f1 => !string.IsNullOrWhiteSpace(f1))
				.Select(f2 => f2.Trim())
				.Select(f3 => f3.ToLowerInvariant())
				.Distinct(StringComparer.InvariantCultureIgnoreCase);

			return filteredFrameworks;
		}
	}
}
