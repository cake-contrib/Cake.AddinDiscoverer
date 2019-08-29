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

		public async Task ExecuteAsync(DiscoveryContext context)
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
							addin.GithubIssueId.HasValue &&
							!addin.GithubPullRequestId.HasValue &&
							!string.IsNullOrEmpty(addin.RepositoryName) &&
							!string.IsNullOrEmpty(addin.RepositoryOwner))
						{
							var filesGroupedByExtention = await Misc.GetFilePathsFromRepoAsync(context, addin).ConfigureAwait(false);
							var commits = new List<(string CommitMessage, IEnumerable<string> FilesToDelete, IEnumerable<(EncodingType Encoding, string Path, string Content)> FilesToUpsert)>();

							await FixNuspec(context, addin, recommendedCakeVersion, filesGroupedByExtention, commits).ConfigureAwait(false);
							await FixCsproj(context, addin, recommendedCakeVersion, filesGroupedByExtention, commits).ConfigureAwait(false);

							if (commits.Any())
							{
								// Make sure we have enough API calls left before proceeding
								var apiInfo = context.GithubClient.GetLastApiInfo();
								var requestsLeft = apiInfo?.RateLimit?.Remaining ?? 0;

								// 250 is an arbitrary threshold that I feel is "safe". Keep in mind that we
								// have 10 concurrent connections making a multitude of calls to GihHub's API
								// so this number must be large enough to allow us to bail out before we exhaust
								// the calls we are allowed to make in an hour
								if (requestsLeft > 250)
								{
									// Fork the addin repo if it hasn't been forked already and make sure it's up to date
									var fork = await context.GithubClient.CreateOrRefreshFork(addin.RepositoryOwner, addin.RepositoryName).ConfigureAwait(false);
									var upstream = fork.Parent;

									// This delay is important to avoid triggering GitHub's abuse protection
									await Task.Delay(1000).ConfigureAwait(false);

									// Commit changes to a new branch and submit PR
									var newBranchName = $"addin_discoverer_{DateTime.UtcNow:yyyy_MM_dd_HH_mm_ss}";
									var pullRequest = await Misc.CommitToNewBranchAndSubmitPullRequestAsync(context, fork, addin.GithubIssueId.Value, newBranchName, Constants.PULL_REQUEST_TITLE, commits).ConfigureAwait(false);

									addin.GithubPullRequestId = pullRequest.Number;
									context.PullRequestsCreatedByCurrentUser.Add(pullRequest);
								}
								else
								{
									Console.WriteLine($"  Only {requestsLeft} GitHub API requests left. Therefore skipping PR for {addin.Name} despite the fact that we have {commits.Count} commits.");
								}
							}

							// This delay is important to avoid triggering GitHub's abuse protection
							await Task.Delay(1000).ConfigureAwait(false);
						}

						return addin;
					}, Constants.MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);
		}

		private async Task FixNuspec(DiscoveryContext context, AddinMetadata addin, CakeVersion cakeVersion, IDictionary<string, string[]> filesPathGroupedByExtension, IList<(string CommitMessage, IEnumerable<string> FilesToDelete, IEnumerable<(EncodingType Encoding, string Path, string Content)> FilesToUpsert)> commits)
		{
			// Get the nuspec files
			filesPathGroupedByExtension.TryGetValue(".nuspec", out string[] filePaths);
			if (filePaths == null || !filePaths.Any()) return;

			// Get the nuspec file
			var filePath = filePaths.FirstOrDefault(path => Path.GetFileName(path).EqualsIgnoreCase($"{addin.Name}.nuspec"));
			if (string.IsNullOrEmpty(filePath)) return;

			// Get the content of the csproj file
			var nuspecFileContent = await Misc.GetFileContentFromRepoAsync(context, addin, filePath).ConfigureAwait(false);

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
				commits.Add(("Fix iconUrl", null, new[] { (EncodingType.Utf8, filePath, document.ToString()) }));
			}
		}

		private async Task FixCsproj(DiscoveryContext context, AddinMetadata addin, CakeVersion cakeVersion, IDictionary<string, string[]> filesPathGroupedByExtension, IList<(string CommitMessage, IEnumerable<string> FilesToDelete, IEnumerable<(EncodingType Encoding, string Path, string Content)> FilesToUpsert)> commits)
		{
			// Get the csproj files
			filesPathGroupedByExtension.TryGetValue(".csproj", out string[] filePaths);
			if (filePaths == null || !filePaths.Any()) return;

			// Loop through the project files
			foreach (var filePath in filePaths)
			{
				// Get the content of the csproj file
				var projectFileContent = await Misc.GetFileContentFromRepoAsync(context, addin, filePath).ConfigureAwait(false);

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
				var isMainProjectFile = Path.GetFileName(filePath).EqualsIgnoreCase($"{addin.Name}.csproj");
				if (isMainProjectFile)
				{
					// Update the package icon
					var packageIconUrl = document.Document.GetFirstElementValue("PackageIconUrl");
					if (packageIconUrl != Constants.NEW_CAKE_CONTRIB_ICON_URL)
					{
						if (document.Document.SetFirstElementValue("PackageIconUrl", Constants.NEW_CAKE_CONTRIB_ICON_URL))
						{
							commits.Add(("Fix PackageIconUrl", null, new[] { (EncodingType.Utf8, filePath, document.ToString()) }));
						}
					}

					// If only one framework is targetted, make sure it's the required one
					var targetFramework = document.Document.GetFirstElementValue("TargetFramework");
					if (!string.IsNullOrEmpty(targetFramework) && targetFramework != cakeVersion.RequiredFramework)
					{
						if (document.Document.SetFirstElementValue("TargetFramework", cakeVersion.RequiredFramework))
						{
							commits.Add(("Fix TargetFramework", null, new[] { (EncodingType.Utf8, filePath, document.ToString()) }));
						}
					}

					// If multiple frameworks are targetted, make sure the required one is among them
					var targetFrameworks = document.Document.GetFirstElementValue("TargetFrameworks")?.Split(';', StringSplitOptions.RemoveEmptyEntries) ?? Enumerable.Empty<string>();
					if (targetFrameworks.Any() && !targetFrameworks.Contains(cakeVersion.RequiredFramework))
					{
						if (document.Document.SetFirstElementValue("TargetFrameworks", cakeVersion.RequiredFramework))
						{
							commits.Add(("Fix TargetFrameworks", null, new[] { (EncodingType.Utf8, filePath, document.ToString()) }));
						}
					}
				}

				// Make sure the right version of Cake.Core, Cake.Common and Cake.Testing is referenced
				FixCakeReferenceInProjectFile(document, "Cake.Core", cakeVersion, filePath, isMainProjectFile, commits);
				FixCakeReferenceInProjectFile(document, "Cake.Common", cakeVersion, filePath, isMainProjectFile, commits);
				FixCakeReferenceInProjectFile(document, "Cake.Testing", cakeVersion, filePath, isMainProjectFile, commits);
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
