using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
using Octokit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class SubmitGithubPullRequest : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => context.Options.SubmitGithubPullRequest;

		public string GetDescription(DiscoveryContext context) => "Submit Github pull requests";

		public async Task ExecuteAsync(DiscoveryContext context, TextWriter log)
		{
			var recommendedCakeVersion = Constants.CAKE_VERSIONS
				.OrderByDescending(cakeVersion => cakeVersion.Version)
				.First();

			context.Addins = await context.Addins
				.OrderBy(a => a.Name)
				.ForEachAsync(
					async addin =>
					{
						if (addin.Type != AddinType.Recipe &&
							addin.AuditIssue != null &&
							addin.AuditPullRequest == null &&
							!string.IsNullOrEmpty(addin.RepositoryName) &&
							!string.IsNullOrEmpty(addin.RepositoryOwner))
						{
							var commits = new List<(string CommitMessage, IEnumerable<string> FilesToDelete, IEnumerable<(EncodingType Encoding, string Path, string Content)> FilesToUpsert)>();

							await FixNuspec(context, addin, recommendedCakeVersion, commits).ConfigureAwait(false);
							await FixCsproj(context, addin, recommendedCakeVersion, commits).ConfigureAwait(false);

							if (commits.Any())
							{
								// Make sure we have enough API calls left before proceeding
								var apiInfo = context.GithubClient.GetLastApiInfo();
								var requestsLeft = apiInfo?.RateLimit?.Remaining ?? 0;

								if (requestsLeft > Constants.MIN_GITHUB_REQUESTS_THRESHOLD)
								{
									// Fork the addin repo if it hasn't been forked already and make sure it's up to date
									var fork = await context.GithubClient.CreateOrRefreshFork(addin.RepositoryOwner, addin.RepositoryName).ConfigureAwait(false);

									// This delay is important to avoid triggering GitHub's abuse protection
									await Misc.RandomGithubDelayAsync().ConfigureAwait(false);

									// Commit changes to a new branch and submit PR
									var newBranchName = $"addin_discoverer_{DateTime.UtcNow:yyyy_MM_dd_HH_mm_ss}";
									var pullRequest = await Misc.CommitToNewBranchAndSubmitPullRequestAsync(context, fork, addin.AuditIssue?.Number, newBranchName, Constants.PULL_REQUEST_TITLE, commits).ConfigureAwait(false);

									if (pullRequest != null)
									{
										addin.AuditPullRequest = pullRequest;
										context.PullRequestsCreatedByCurrentUser.Add(pullRequest);
									}

									// This delay is important to avoid triggering GitHub's abuse protection
									await Misc.RandomGithubDelayAsync().ConfigureAwait(false);
								}
								else
								{
									Console.WriteLine($"  Only {requestsLeft} GitHub API requests left. Therefore skipping PR for {addin.Name} despite the fact that we have {commits.Count} commits.");
								}
							}
						}

						return addin;
					}, Constants.MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);
		}

		private async Task FixNuspec(DiscoveryContext context, AddinMetadata addin, CakeVersion cakeVersion, IList<(string CommitMessage, IEnumerable<string> FilesToDelete, IEnumerable<(EncodingType Encoding, string Path, string Content)> FilesToUpsert)> commits)
		{
			// Get the nuspec file
			var nuspecFiles = addin.RepoContent
				.Where(item => Path.GetFileName(item.Key).EqualsIgnoreCase($"{addin.Name}.nuspec"));
			if (!nuspecFiles.Any()) return;
			var nuspecFile = nuspecFiles.First();

			// Get the content of the nuspec file
			nuspecFile.Value.Position = 0;
			var nuspecFileContent = await new StreamReader(nuspecFile.Value).ReadToEndAsync().ConfigureAwait(false);

			// Parse the content of the nuspec file
			var document = new XDocumentFormatPreserved(nuspecFileContent);

			var packageElement = document.Document.Element("package");
			if (packageElement == null) return;

			var metadataElement = packageElement.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata");
			if (metadataElement == null) return;

			var iconUrlElement = metadataElement.Elements().FirstOrDefault(e => e.Name.LocalName == "iconUrl");
			if (iconUrlElement != null && iconUrlElement.Value != Constants.NEW_CAKE_CONTRIB_ICON_URL)
			{
				iconUrlElement.SetValue(Constants.NEW_CAKE_CONTRIB_ICON_URL);
				commits.Add(("Fix iconUrl", null, new[] { (EncodingType.Utf8, nuspecFile.Key, document.ToString()) }));
			}
		}

		private async Task FixCsproj(DiscoveryContext context, AddinMetadata addin, CakeVersion cakeVersion, IList<(string CommitMessage, IEnumerable<string> FilesToDelete, IEnumerable<(EncodingType Encoding, string Path, string Content)> FilesToUpsert)> commits)
		{
			// Get the csproj files
			var projectFiles = addin.RepoContent
				.Where(item => Path.GetExtension(item.Key).EqualsIgnoreCase(".csproj"));
			if (!projectFiles.Any()) return;

			// Loop through the project files
			foreach (var projectFile in projectFiles)
			{
				// Get the content of the csproj file
				projectFile.Value.Position = 0;
				var projectFileContent = await new StreamReader(projectFile.Value).ReadToEndAsync().ConfigureAwait(false);

				// Parse the content of the csproj file
				var document = new XDocumentFormatPreserved(projectFileContent);

				// Make sure it's a VS 2017 project file
				var sdkAttribute = (from attribute in document.Document.Root?.Attributes()
									where attribute.Name.LocalName.EqualsIgnoreCase("Sdk")
									select attribute).FirstOrDefault();

				// Make sure we are dealing with a VS 2017 project file
				if (sdkAttribute == null) return;

				// Some rules are different in the "main" project as opposed to unit tests project, integration tests project, etc.
				// For instance, we only update the icon in the main project.
				var isMainProjectFile = Path.GetFileName(projectFile.Key).EqualsIgnoreCase($"{addin.Name}.csproj");
				if (isMainProjectFile)
				{
					// Update the package icon
					var packageIconUrl = document.Document.GetFirstElementValue("PackageIconUrl");
					if (packageIconUrl != Constants.NEW_CAKE_CONTRIB_ICON_URL)
					{
						if (document.Document.SetFirstElementValue("PackageIconUrl", Constants.NEW_CAKE_CONTRIB_ICON_URL))
						{
							commits.Add(("Fix PackageIconUrl", null, new[] { (EncodingType.Utf8, projectFile.Key, document.ToString()) }));
						}
					}

					// If only one framework is targetted, make sure it's the required one
					var targetFramework = document.Document.GetFirstElementValue("TargetFramework");
					if (!string.IsNullOrEmpty(targetFramework) && cakeVersion.RequiredFramework.Contains(targetFramework, StringComparer.OrdinalIgnoreCase))
					{
						if (document.Document.SetFirstElementValue("TargetFramework", cakeVersion.RequiredFramework[0]))
						{
							commits.Add(("Fix TargetFramework", null, new[] { (EncodingType.Utf8, projectFile.Key, document.ToString()) }));
						}
					}

					// If multiple frameworks are targetted, make sure the required one is among them
					var targetFrameworks = document.Document.GetFirstElementValue("TargetFrameworks")?.Split(';', StringSplitOptions.RemoveEmptyEntries) ?? Enumerable.Empty<string>();
					if (targetFrameworks.Any() && !targetFrameworks.Contains(cakeVersion.RequiredFramework[0]))
					{
						if (document.Document.SetFirstElementValue("TargetFrameworks", cakeVersion.RequiredFramework[0]))
						{
							commits.Add(("Fix TargetFrameworks", null, new[] { (EncodingType.Utf8, projectFile.Key, document.ToString()) }));
						}
					}
				}

				// Make sure the right version of Cake.Core, Cake.Common and Cake.Testing is referenced
				FixCakeReferenceInProjectFile(document, "Cake.Core", cakeVersion, projectFile.Key, isMainProjectFile, commits);
				FixCakeReferenceInProjectFile(document, "Cake.Common", cakeVersion, projectFile.Key, isMainProjectFile, commits);
				FixCakeReferenceInProjectFile(document, "Cake.Testing", cakeVersion, projectFile.Key, isMainProjectFile, commits);
			}
		}

		private void FixCakeReferenceInProjectFile(XDocumentFormatPreserved document, string referenceName, CakeVersion cakeVersion, string filePath, bool isMainProjectFile, IList<(string CommitMessage, IEnumerable<string> FilesToDelete, IEnumerable<(EncodingType Encoding, string Path, string Content)> FilesToUpsert)> commits)
		{
			var ns = document.Document.Root?.Name.Namespace;
			var packageReferenceXName = ns.GetXNameWithNamespace("PackageReference");
			var privateAssetsXName = ns.GetXNameWithNamespace("PrivateAssets");
			var packageReferences = document.Document.Descendants(packageReferenceXName);

			var cakeReference = packageReferences.FirstOrDefault(r => r.Attribute("Include")?.Value?.Equals(referenceName) ?? false);
			if (cakeReference != null)
			{
				var versionAttribute = cakeReference.Attribute("Version");
				if (versionAttribute != null)
				{
					var referencedVersion = SemVersion.Parse(versionAttribute.Value);
					if (!referencedVersion.IsUpToDate(cakeVersion.Version))
					{
						cakeReference.SetAttributeValue("Version", cakeVersion.Version.ToString(3));
						commits.Add(($"Upgrade {referenceName} reference to {cakeVersion.Version.ToString(3)}", null, new[] { (EncodingType.Utf8, filePath, document.ToString()) }));
					}
				}

				if (isMainProjectFile)
				{
					var privateAssetsElement = cakeReference.Element(privateAssetsXName);
					if (privateAssetsElement != null)
					{
						if (!privateAssetsElement.Value.EqualsIgnoreCase("All"))
						{
							privateAssetsElement.SetValue("All");
							commits.Add(($"{referenceName} reference should be private", null, new[] { (EncodingType.Utf8, filePath, document.ToString()) }));
						}
					}
					else
					{
						var privateAssetsAttribute = cakeReference.Attribute("PrivateAssets");
						if (privateAssetsAttribute == null || !privateAssetsAttribute.Value.EqualsIgnoreCase("All"))
						{
							cakeReference.SetAttributeValue("PrivateAssets", "All");
							commits.Add(($"{referenceName} reference should be private", null, new[] { (EncodingType.Utf8, filePath, document.ToString()) }));
						}
					}
				}
			}
		}
	}
}
