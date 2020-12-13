using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using Octokit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.RepresentationModel;

namespace Cake.AddinDiscoverer.Steps
{
	internal class SynchronizeYamlStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => context.Options.SynchronizeYaml;

		public string GetDescription(DiscoveryContext context) => "Synchronize yml files on the Cake web site";

		public async Task ExecuteAsync(DiscoveryContext context)
		{
			// Arbitrary max number of files to delete, add and modify in a given commit.
			// This is to avoid AbuseException when commiting too many files.
			const int MAX_FILES_TO_COMMIT = 75;

			// Ensure the fork is up-to-date
			var fork = await context.GithubClient.CreateOrRefreshFork(Constants.CAKE_REPO_OWNER, Constants.CAKE_WEBSITE_REPO_NAME).ConfigureAwait(false);
			var upstream = fork.Parent;

			// --------------------------------------------------
			// Discover if any files need to be added/deleted/modified
			var directoryContent = await context.GithubClient.Repository.Content.GetAllContents(Constants.CAKE_REPO_OWNER, Constants.CAKE_WEBSITE_REPO_NAME, "extensions").ConfigureAwait(false);
			var yamlFiles = directoryContent
				.Where(file => file.Name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
				.Where(file => string.IsNullOrEmpty(context.Options.AddinName) || Path.GetFileNameWithoutExtension(file.Name) == context.Options.AddinName)
				.ToArray();

			var yamlsToBeDeleted = yamlFiles
				.Where(f =>
				{
					var addin = context.Addins.FirstOrDefault(a => a.Name == Path.GetFileNameWithoutExtension(f.Name));
					return addin == null || addin.IsDeprecated;
				})
				.OrderBy(f => f.Name)
				.Take(MAX_FILES_TO_COMMIT)
				.ToArray();

			var addinsWithContent = await context.Addins
				.Where(addin => !addin.IsDeprecated)
				.Where(addin => addin.Type == AddinType.Addin)
				.Where(addin => yamlFiles.Any(f => Path.GetFileNameWithoutExtension(f.Name) == addin.Name))
				.ForEachAsync(
					async addin =>
					{
						var contents = await context.GithubClient.Repository.Content.GetAllContents(Constants.CAKE_REPO_OWNER, Constants.CAKE_WEBSITE_REPO_NAME, $"extensions/{addin.Name}.yml").ConfigureAwait(false);
						return new
						{
							Addin = addin,
							CurrentContent = contents[0].Content,
							NewContent = UpdateYamlFile(context, addin, contents[0].Content)
						};
					}, Constants.MAX_NUGET_CONCURENCY)
				.ConfigureAwait(false);

			var addinsToBeUpdated = addinsWithContent
				.Where(addin => addin.CurrentContent != addin.NewContent)
				.OrderBy(addin => addin.Addin.Name)
				.Take(MAX_FILES_TO_COMMIT)
				.ToArray();

			var addinsToBeCreated = context.Addins
				.Where(addin => !addin.IsDeprecated)
				.Where(addin => addin.Type == AddinType.Addin)
				.Where(addin => !yamlFiles.Any(f => Path.GetFileNameWithoutExtension(f.Name) == addin.Name))
				.OrderBy(addin => addin.Name)
				.Select(addin => new
				{
					Addin = addin,
					CurrentContent = string.Empty,
					NewContent = GenerateYamlFile(context, addin)
				})
				.Where(addin => !string.IsNullOrEmpty(addin.NewContent))
				.Take(MAX_FILES_TO_COMMIT)
				.ToArray();

			if (yamlsToBeDeleted.Any())
			{
				var apiInfo = context.GithubClient.GetLastApiInfo();
				var requestsLeft = apiInfo?.RateLimit?.Remaining ?? 0;

				var threshold = yamlsToBeDeleted.Count() * 5;
				if (requestsLeft < threshold)
				{
					Console.WriteLine($"  Only {requestsLeft} GitHub API requests left. Therefore skipping PRs to delete yaml files.");
				}

				foreach (var yamlToBeDeleted in yamlsToBeDeleted)
				{
					var issueTitle = $"Delete {yamlToBeDeleted.Name}";

					// Check if an issue already exists
					var issue = await Misc.FindGithubIssueAsync(context, upstream.Owner.Login, upstream.Name, context.Options.GithubUsername, issueTitle).ConfigureAwait(false);
					if (issue == null)
					{
						// Create issue
						var newIssue = new NewIssue(issueTitle)
						{
							Body = $"The Cake.AddinDiscoverer tool has discovered that a YAML file exists on the Cake web site for a nuget package that is no longer available on NuGet.org.{Environment.NewLine}" +
								$"{Environment.NewLine}{yamlToBeDeleted.Name}.yaml must be deleted.{Environment.NewLine}"
						};
						issue = await context.GithubClient.Issue.Create(Constants.CAKE_REPO_OWNER, Constants.CAKE_WEBSITE_REPO_NAME, newIssue).ConfigureAwait(false);
						context.IssuesCreatedByCurrentUser.Add(issue);

						// Commit changes to a new branch and submit PR
						var newBranchName = $"delete_{yamlToBeDeleted.Name}_{DateTime.UtcNow:yyyy_MM_dd_HH_mm_ss}";
						var commits = new List<(string CommitMessage, IEnumerable<string> FilesToDelete, IEnumerable<(EncodingType Encoding, string Path, string Content)> FilesToUpsert)>
						{
							(CommitMessage: issueTitle, FilesToDelete: new[] { yamlToBeDeleted.Path }, FilesToUpsert: null)
						};

						var pullRequest = await Misc.CommitToNewBranchAndSubmitPullRequestAsync(context, fork, issue?.Number, newBranchName, issueTitle, commits).ConfigureAwait(false);
						context.PullRequestsCreatedByCurrentUser.Add(pullRequest);

						// This delay is important to avoid triggering GitHub's abuse protection
						await Task.Delay(1000).ConfigureAwait(false);
					}
				}
			}

			if (addinsToBeCreated.Any())
			{
				var apiInfo = context.GithubClient.GetLastApiInfo();
				var requestsLeft = apiInfo?.RateLimit?.Remaining ?? 0;

				var threshold = addinsToBeCreated.Count() * 5;
				if (requestsLeft < threshold)
				{
					Console.WriteLine($"  Only {requestsLeft} GitHub API requests left. Therefore skipping PRs to add yaml files.");
				}

				foreach (var addinToBeCreated in addinsToBeCreated)
				{
					var issueTitle = $"Add {addinToBeCreated.Addin.Name}.yml";

					// Check if an issue already exists
					var issue = await Misc.FindGithubIssueAsync(context, upstream.Owner.Login, upstream.Name, context.Options.GithubUsername, issueTitle).ConfigureAwait(false);
					if (issue == null)
					{
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
						context.PullRequestsCreatedByCurrentUser.Add(pullRequest);

						// This delay is important to avoid triggering GitHub's abuse protection
						await Task.Delay(1000).ConfigureAwait(false);
					}
				}
			}

			if (addinsToBeUpdated.Any())
			{
				var apiInfo = context.GithubClient.GetLastApiInfo();
				var requestsLeft = apiInfo?.RateLimit?.Remaining ?? 0;

				var threshold = addinsToBeUpdated.Count() * 5;
				if (requestsLeft < threshold)
				{
					Console.WriteLine($"  Only {requestsLeft} GitHub API requests left. Therefore skipping PRs to update yaml files.");
				}

				foreach (var addinToBeUpdated in addinsToBeUpdated)
				{
					var issueTitle = $"Update {addinToBeUpdated.Addin.Name}.yml";

					// Check if an issue already exists
					var issue = await Misc.FindGithubIssueAsync(context, upstream.Owner.Login, upstream.Name, context.Options.GithubUsername, issueTitle).ConfigureAwait(false);
					if (issue == null)
					{
						// Create issue
						var newIssue = new NewIssue(issueTitle)
						{
							Body = $"The Cake.AddinDiscoverer tool has discovered discrepancies between {addinToBeUpdated.Addin.Name}.yaml on Cake's web site and the metadata in the packages discovered on NuGet.org.{Environment.NewLine}" +
									$"{Environment.NewLine}{addinToBeUpdated.Addin.Name}.yaml must be updated.{Environment.NewLine}"
						};
						issue = await context.GithubClient.Issue.Create(Constants.CAKE_REPO_OWNER, Constants.CAKE_WEBSITE_REPO_NAME, newIssue).ConfigureAwait(false);
						context.IssuesCreatedByCurrentUser.Add(issue);

						// Commit changes to a new branch and submit PR
						var newBranchName = $"update_{addinToBeUpdated.Addin.Name}.yml_{DateTime.UtcNow:yyyy_MM_dd_HH_mm_ss}";
						var commits = new List<(string CommitMessage, IEnumerable<string> FilesToDelete, IEnumerable<(EncodingType Encoding, string Path, string Content)> FilesToUpsert)>
						{
							(CommitMessage: issueTitle, FilesToDelete: null, FilesToUpsert: new[] { (Encoding: EncodingType.Utf8, Path: $"extensions/{addinToBeUpdated.Addin.Name}.yml", Content: addinToBeUpdated.NewContent) })
						};

						var pullRequest = await Misc.CommitToNewBranchAndSubmitPullRequestAsync(context, fork, issue?.Number, newBranchName, issueTitle, commits).ConfigureAwait(false);
						context.PullRequestsCreatedByCurrentUser.Add(pullRequest);

						// This delay is important to avoid triggering GitHub's abuse protection
						await Task.Delay(1000).ConfigureAwait(false);
					}
				}
			}
		}

		private static string GenerateYamlFile(DiscoveryContext context, AddinMetadata addin)
		{
			var categories = GetCategoriesForYaml(context, addin.Tags);

			if (addin.ProjectUrl == null) return null;

			var description = string.IsNullOrEmpty(addin.Description) ? Constants.NO_DESCRIPTION_PROVIDED : addin.Description;

			var yamlContent = new StringBuilder();
			yamlContent.AppendUnixLine($"Type: {addin.Type}");
			yamlContent.AppendUnixLine($"Name: {addin.Name}");
			yamlContent.AppendUnixLine($"NuGet: {addin.Name}");
			yamlContent.AppendUnixLine("Assemblies:");
			yamlContent.AppendUnixLine($"- \"/**/{addin.DllName}\"");
			yamlContent.AppendUnixLine($"Repository: {addin.ProjectUrl.AbsoluteUri.TrimEnd('/') + '/'}");
			yamlContent.AppendUnixLine($"Author: {addin.GetMaintainerName()}");
			yamlContent.AppendUnixLine($"Description: {QuotedYamlString(description)}");
			yamlContent.AppendUnixLine("Categories:");
			yamlContent.AppendUnixLine(categories);
			yamlContent.AppendUnixLine($"TargetCakeVersion: {addin.AnalysisResult.GetTargetedCakeVersion()?.ToString()}");
			yamlContent.AppendUnixLine($"AnalyzedPackageVersion: {addin.NuGetPackageVersion}");

			return yamlContent.ToString();
		}

		private static string UpdateYamlFile(DiscoveryContext context, AddinMetadata addin, string currentYamlContent)
		{
			var input = new StringReader(currentYamlContent);
			var yaml = new YamlStream();
			yaml.Load(input);

			var mapping = (YamlMappingNode)yaml.Documents[0].RootNode;

			var yamlContent = new StringBuilder();
			yamlContent.AppendUnixLine($"Type: {addin.Type}");
			yamlContent.AppendUnixLine($"Name: {mapping.GetChildNodeValue("Name")}");
			yamlContent.AppendUnixLine($"NuGet: {mapping.GetChildNodeValue("NuGet")}");

			yamlContent.AppendUnixLine("Assemblies:");
			foreach (var childNodeValue in mapping.GetChildrenNodesValue("Assemblies"))
			{
				yamlContent.AppendUnixLine($"- \"{childNodeValue}\"");
			}

			yamlContent.AppendUnixLine($"Repository: {(addin.ProjectUrl ?? addin.NuGetPackageUrl).AbsoluteUri.TrimEnd('/') + '/'}");
			yamlContent.AppendUnixLine($"Author: {mapping.GetChildNodeValue("Author")}");
			yamlContent.AppendUnixLine($"Description: {QuotedYamlString(mapping.GetChildNodeValue("Description"))}");
			yamlContent.AppendUnixLine("Categories:");
			yamlContent.AppendUnixLine(GetCategoriesForYaml(context, mapping));
			yamlContent.AppendUnixLine($"TargetCakeVersion: {addin.AnalysisResult.GetTargetedCakeVersion().ToString()}");
			yamlContent.AppendUnixLine($"AnalyzedPackageVersion: {addin.NuGetPackageVersion}");

			return yamlContent.ToString();
		}

		private static string GetCategoriesForYaml(DiscoveryContext context, YamlMappingNode mapping)
		{
			var key = new YamlScalarNode("Categories");
			if (!mapping.Children.ContainsKey(key)) return string.Empty;

			var tags = Enumerable.Empty<string>();
			if (mapping.Children[key] is YamlScalarNode scalarNode) tags = new[] { scalarNode.ToString() };
			else if (mapping.Children[key] is YamlSequenceNode sequenceNode) tags = sequenceNode.Select(node => node.ToString());

			return GetCategoriesForYaml(context, tags);
		}

		private static string GetCategoriesForYaml(DiscoveryContext context, IEnumerable<string> tags)
		{
			var filteredAndFormattedTags = tags
				.Where(t1 => !string.IsNullOrWhiteSpace(t1))
				.Select(t2 => t2.Trim())
				.Select(t3 => t3.ToLowerInvariant())
				.Except(context.ExcludedTags, StringComparer.InvariantCultureIgnoreCase)
				.Distinct(StringComparer.InvariantCultureIgnoreCase)
				.Select(tag => $"- {tag}");

			var categories = string.Join("\n", filteredAndFormattedTags);

			return categories;
		}

		private static string QuotedYamlString(string value)
		{
			if (value.StartsWith('"') && value.EndsWith('"'))
			{
				return value;
			}
			else if (value.Contains("\n"))
			{
				var lines = value
					.Replace("\r\n", "\n")
					.Split('\n')
					.Select(line => string.IsNullOrWhiteSpace(line) ? string.Empty : $"  {line}");
				return "|-\n" + string.Join('\n', lines);
			}
			else
			{
				return value;
			}
		}
	}
}
