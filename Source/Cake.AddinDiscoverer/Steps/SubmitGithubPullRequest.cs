using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
using Octokit;
using System;
using System.Collections.Generic;
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
				.ForEachAsync(
					async addin =>
					{
						if (addin.Type != AddinType.Recipe &&
							addin.GithubIssueId.HasValue &&
							!string.IsNullOrEmpty(addin.GithubRepoName) &&
							!string.IsNullOrEmpty(addin.GithubRepoOwner))
						{
							var extensions = new[] { ".csproj", ".nuspec" };
							var files = await GetFilesFromRepoAsync(context, addin, extensions, null).ConfigureAwait(false);

							var commits = new List<(string CommitMessage, IEnumerable<string> FilesToDelete, IEnumerable<(EncodingType Encoding, string Path, string Content)> FilesToUpsert)>();

							await FixNuspecFile(context, addin, recommendedCakeVersion, files, commits).ConfigureAwait(false);
							await FixProjectFile(context, addin, recommendedCakeVersion, files, commits).ConfigureAwait(false);

							if (commits.Any())
							{
								// Fork the addin repo if it hasn't been forked already and make sure it's up to date
								var fork = await context.GithubClient.CreateOrRefreshFork(addin.GithubRepoOwner, addin.GithubRepoName).ConfigureAwait(false);
								var upstream = fork.Parent;

								// Commit changes to a new branch and submit PR
								var newBranchName = $"addin_discoverer_{DateTime.UtcNow:yyyy_MM_dd_HH_mm_ss}";
								var pullRequestTitle = "Fix issues identified by automated audit";
								await Misc.CommitToNewBranchAndSubmitPullRequestAsync(context, fork, addin.GithubIssueId.Value, newBranchName, pullRequestTitle, commits).ConfigureAwait(false);
							}
						}

						return addin;
					}, Constants.MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);
		}

		private async Task<RepositoryContent[]> GetFilesFromRepoAsync(DiscoveryContext context, AddinMetadata addin, IEnumerable<string> extensions, string folderName = null)
		{
			var files = new List<RepositoryContent>();

			var directoryContent = string.IsNullOrEmpty(folderName) ?
					await context.GithubClient.Repository.Content.GetAllContents(addin.GithubRepoOwner, addin.GithubRepoName).ConfigureAwait(false) :
					await context.GithubClient.Repository.Content.GetAllContents(addin.GithubRepoOwner, addin.GithubRepoName, folderName).ConfigureAwait(false);

			var filesInRoot = directoryContent.Where(c => c.Type == new StringEnum<ContentType>(ContentType.File) && extensions.Contains(System.IO.Path.GetExtension(c.Name), StringComparer.OrdinalIgnoreCase));
			files.AddRange(filesInRoot);

			var subFolders = directoryContent.Where(c => c.Type == new StringEnum<ContentType>(ContentType.Dir));
			foreach (var subFolder in subFolders)
			{
				var filesInSubFolder = await GetFilesFromRepoAsync(context, addin, extensions, subFolder.Path).ConfigureAwait(false);
				files.AddRange(filesInSubFolder);
			}

			return files.ToArray();
		}

		private async Task<string> GetFileContentFromRepoAsync(DiscoveryContext context, AddinMetadata addin, string filePath)
		{
			var content = await context.GithubClient.Repository.Content.GetAllContents(addin.GithubRepoOwner, addin.GithubRepoName, filePath).ConfigureAwait(false);
			return content[0].Content;
		}

		private async Task FixNuspecFile(DiscoveryContext context, AddinMetadata addin, CakeVersion cakeVersion, RepositoryContent[] files, IList<(string CommitMessage, IEnumerable<string> FilesToDelete, IEnumerable<(EncodingType Encoding, string Path, string Content)> FilesToUpsert)> commits)
		{
			// Get the nuspec file
			var nuspecFile = files.FirstOrDefault(file => file.Name.Equals($"{addin.Name}.nuspec", StringComparison.OrdinalIgnoreCase));
			if (nuspecFile == null) return;

			// Get the content of the csproj file
			var nuspecFileContent = await GetFileContentFromRepoAsync(context, addin, nuspecFile.Path).ConfigureAwait(false);

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
				commits.Add(("Fix iconUrl", null, new[] { (EncodingType.Utf8, nuspecFile.Path, document.ToString()) }));
			}
		}

		private async Task FixProjectFile(DiscoveryContext context, AddinMetadata addin, CakeVersion cakeVersion, RepositoryContent[] files, IList<(string CommitMessage, IEnumerable<string> FilesToDelete, IEnumerable<(EncodingType Encoding, string Path, string Content)> FilesToUpsert)> commits)
		{
			// Get the project file
			var projectFile = files.FirstOrDefault(file => file.Name.Equals($"{addin.Name}.csproj", StringComparison.OrdinalIgnoreCase));
			if (projectFile == null) return;

			// Get the content of the csproj file
			var projectFileContent = await GetFileContentFromRepoAsync(context, addin, projectFile.Path).ConfigureAwait(false);

			// Parse the content of the csproj file
			var document = new XDocumentFormatPreserved(projectFileContent);

			// Make sure it's a VS 2017 project file
			var sdkAttribute = (from attribute in document.Document.Root?.Attributes()
								where attribute.Name.LocalName.EqualsIgnoreCase("Sdk")
								select attribute).FirstOrDefault();

			// Make sure we are dealing with a VS 2017 project file
			if (sdkAttribute == null) return;

			var packageIconUrl = document.Document.GetFirstElementValue("PackageIconUrl");
			if (packageIconUrl != Constants.NEW_CAKE_CONTRIB_ICON_URL)
			{
				if (document.Document.SetFirstElementValue("PackageIconUrl", Constants.NEW_CAKE_CONTRIB_ICON_URL))
				{
					commits.Add(("Fix PackageIconUrl", null, new[] { (EncodingType.Utf8, projectFile.Path, document.ToString()) }));
				}
			}

			var targetFramework = document.Document.GetFirstElementValue("TargetFramework");
			if (!string.IsNullOrEmpty(targetFramework) && targetFramework != cakeVersion.RequiredFramework)
			{
				if (document.Document.SetFirstElementValue("TargetFramework", cakeVersion.RequiredFramework))
				{
					commits.Add(("Fix TargetFramework", null, new[] { (EncodingType.Utf8, projectFile.Path, document.ToString()) }));
				}
			}

			var targetFrameworks = document.Document.GetFirstElementValue("TargetFrameworks")?.Split(';', StringSplitOptions.RemoveEmptyEntries) ?? Enumerable.Empty<string>();
			if (!targetFrameworks.Contains(cakeVersion.RequiredFramework))
			{
				if (document.Document.SetFirstElementValue("TargetFrameworks", cakeVersion.RequiredFramework))
				{
					commits.Add(("Fix TargetFrameworks", null, new[] { (EncodingType.Utf8, projectFile.Path, document.ToString()) }));
				}
			}

			FixCakeReferenceInProjectFile(document, "Cake.Core", cakeVersion, projectFile, commits);
			FixCakeReferenceInProjectFile(document, "Cake.Common", cakeVersion, projectFile, commits);
		}

		private void FixCakeReferenceInProjectFile(XDocumentFormatPreserved document, string referenceName, CakeVersion cakeVersion, RepositoryContent projectFile, IList<(string CommitMessage, IEnumerable<string> FilesToDelete, IEnumerable<(EncodingType Encoding, string Path, string Content)> FilesToUpsert)> commits)
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
						commits.Add(($"Upgrade {referenceName} reference to {cakeVersion.Version.ToString(3)}", null, new[] { (EncodingType.Utf8, projectFile.Path, document.ToString()) }));
					}
				}

				var privateAssetsElement = cakeReference.Element(privateAssetsXName);
				if (privateAssetsElement != null)
				{
					if (!privateAssetsElement.Value.Equals("All"))
					{
						privateAssetsElement.SetValue("All");
						commits.Add(($"{referenceName} reference should be private", null, new[] { (EncodingType.Utf8, projectFile.Path, document.ToString()) }));
					}
				}
				else
				{
					var privateAssetsAttribute = cakeReference.Attribute("PrivateAssets");
					if (privateAssetsAttribute == null || !privateAssetsAttribute.Value.Equals("All"))
					{
						cakeReference.SetAttributeValue("PrivateAssets", "All");
						commits.Add(($"{referenceName} reference should be private", null, new[] { (EncodingType.Utf8, projectFile.Path, document.ToString()) }));
					}
				}
			}
		}
	}
}
