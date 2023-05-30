using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
using Newtonsoft.Json.Linq;
using NuGet.Common;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using Octokit;
using Octokit.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class AnalyzeStep : IStep
	{
		private const string PackageOwnersJsonFileUri = "https://nugetprod0.blob.core.windows.net/ng-search-data/owners.json";

		// https://github.com/dotnet/roslyn/blob/b3cbe7abce7633e45d7dd468bde96bfe24ccde47/src/Dependencies/CodeAnalysis.Debugging/PortableCustomDebugInfoKinds.cs#L18
		private static readonly Guid SourceLinkCustomDebugInfoGuid = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");

		public bool PreConditionIsMet(DiscoveryContext context) => true;

		public string GetDescription(DiscoveryContext context) => "Analize addins";

		public async Task ExecuteAsync(DiscoveryContext context, TextWriter log, CancellationToken cancellationToken)
		{
			var nugetPackageDownloadClient = context.NugetRepository.GetResource<DownloadResource>();

			var ownersFileJsonContent = await context.HttpClient.GetStringAsync(PackageOwnersJsonFileUri, cancellationToken).ConfigureAwait(false);
			var packageOwners = JArray.Parse(ownersFileJsonContent)
				.ToDictionary(
					e => e[0].Value<string>(),
					e => e[1].Values<string>().ToArray());

			var cakeContribRepositories = await context.GithubClient.Repository.GetAllForUser(Constants.CAKE_CONTRIB_REPO_OWNER).ConfigureAwait(false);

			var cakeContribIcon = await context.HttpClient.GetByteArrayAsync(Constants.NEW_CAKE_CONTRIB_ICON_URL, cancellationToken).ConfigureAwait(false);
			var fancyIcons = new[]
			{
				new KeyValuePair<AddinType, byte[]>(AddinType.Addin, await context.HttpClient.GetByteArrayAsync(Constants.CAKE_CONTRIB_ADDIN_FANCY_ICON_URL, cancellationToken).ConfigureAwait(false)),
				new KeyValuePair<AddinType, byte[]>(AddinType.Module, await context.HttpClient.GetByteArrayAsync(Constants.CAKE_CONTRIB_MODULE_FANCY_ICON_URL, cancellationToken).ConfigureAwait(false)),
				new KeyValuePair<AddinType, byte[]>(AddinType.Recipe, await context.HttpClient.GetByteArrayAsync(Constants.CAKE_CONTRIB_RECIPE_FANCY_ICON_URL, cancellationToken).ConfigureAwait(false)),
				new KeyValuePair<AddinType, byte[]>(AddinType.Recipe, await context.HttpClient.GetByteArrayAsync(Constants.CAKE_CONTRIB_FROSTINGRECIPE_FANCY_ICON_URL, cancellationToken).ConfigureAwait(false)),
				new KeyValuePair<AddinType, byte[]>(AddinType.All, await context.HttpClient.GetByteArrayAsync(Constants.CAKE_CONTRIB_COMMUNITY_FANCY_ICON_URL, cancellationToken).ConfigureAwait(false))
			};

			var cakeRecipeAddin = context.Addins
				.Where(a => a.Type == AddinType.Recipe)
				.OrderByDescending(a => a.PublishedOn)
				.FirstOrDefault(a => a.Name.EqualsIgnoreCase("Cake.Recipe"));

			var latestCakeRecipeVersion = SemVersion.Parse(cakeRecipeAddin == null ? "0.0.0" : cakeRecipeAddin.NuGetPackageVersion);

			await context.Addins
				.Where(addin => !addin.Analyzed) // Analysis needs to be performed only on new addin versions
				.ForEachAsync(
					async addin =>
					{
						// Download the package from NuGet if it's not already in the cache
						await DownloadNugetPackage(nugetPackageDownloadClient, context, addin).ConfigureAwait(false);
						await DownloadSymbolsPackage(context, addin).ConfigureAwait(false);

						// Analyze the metadata in the downloaded nuget package
						AnalyzeNuGet(context, addin);

						// Set the owners of the NuGet package
						DeterminePackageOwners(packageOwners, addin);

						// Some addins were moved to the cake-contrib organization but the URL in their package metadata still
						// points to the original repo. This step corrects the URL to ensure it points to the right repo.
						// Also, this step forces HTTPS for github URLs.
						await ValidateUrls(context, addin, cakeContribRepositories).ConfigureAwait(false);

						// Check if this addin is using Cake.Recipe
						await CheckUsingCakeRecipe(context, addin, latestCakeRecipeVersion).ConfigureAwait(false);

						// Determine if the addin meets the best practices
						AnalyzeAddin(context, addin, cakeContribIcon, fancyIcons);

						// Mark the addin as 'analyzed'
						addin.Analyzed = true;

						// Save the result of the analysis for each addin to a file to ensure that we
						// can resume the analysis without analysing the same addins multiple times.
						SaveAnalysis(context, addin);
					},
					Constants.MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);
		}

		private static void SaveAnalysis(DiscoveryContext context, AddinMetadata addin)
		{
			var analysisFileName = Path.Combine(context.AnalysisFolder, $"{addin.Name}.{addin.NuGetPackageVersion}.json");
			var jsonFilePath = Path.Combine(context.TempFolder, analysisFileName);
			using FileStream jsonFileStream = File.Create(jsonFilePath);
			JsonSerializer.Serialize(jsonFileStream, addin, new JsonSerializerOptions { WriteIndented = true });
		}

		private static async Task DownloadNugetPackage(DownloadResource nugetClient, DiscoveryContext context, AddinMetadata package)
		{
			var packageFileName = Path.Combine(context.PackagesFolder, $"{package.Name}.{package.NuGetPackageVersion}.nupkg");
			if (!File.Exists(packageFileName))
			{
				// Download the package
				using var sourceCacheContext = new SourceCacheContext() { NoCache = true };
				var downloadContext = new PackageDownloadContext(sourceCacheContext, Path.GetTempPath(), true);
				var packageIdentity = new PackageIdentity(package.Name, new NuGet.Versioning.NuGetVersion(package.NuGetPackageVersion));

				using var result = await nugetClient.GetDownloadResourceResultAsync(packageIdentity, downloadContext, string.Empty, NullLogger.Instance, CancellationToken.None).ConfigureAwait(false);
				switch (result.Status)
				{
					case DownloadResourceResultStatus.Cancelled:
						throw new OperationCanceledException();
					case DownloadResourceResultStatus.NotFound:
						throw new Exception($"Package '{package.Name} {package.NuGetPackageVersion}' not found");
					default:
						{
							await using var fileStream = File.OpenWrite(packageFileName);
							await result.PackageStream.CopyToAsync(fileStream).ConfigureAwait(false);
							break;
						}
				}
			}
		}

		private static async Task DownloadSymbolsPackage(DiscoveryContext context, AddinMetadata addin)
		{
			var packageFileName = Path.Combine(context.PackagesFolder, $"{addin.Name}.{addin.NuGetPackageVersion}.snupkg");
			if (!File.Exists(packageFileName))
			{
				// Download the symbols package
				try
				{
					var response = await context.HttpClient
						.GetAsync($"https://www.nuget.org/api/v2/symbolpackage/{addin.Name}/{addin.NuGetPackageVersion}")
						.ConfigureAwait(false);

					if (response.IsSuccessStatusCode)
					{
						await using var getStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
						await using var fileStream = File.OpenWrite(packageFileName);
						await getStream.CopyToAsync(fileStream).ConfigureAwait(false);
					}
				}
				catch (Exception e)
				{
					throw new Exception($"An error occured while attempting to download symbol package for '{addin.Name} {addin.NuGetPackageVersion}'", e);
				}
			}
		}

		private static void AnalyzeNuGet(DiscoveryContext context, AddinMetadata addin)
		{
			try
			{
				var packageFileName = Path.Combine(context.PackagesFolder, $"{addin.Name}.{addin.NuGetPackageVersion}.nupkg");
				if (File.Exists(packageFileName))
				{
					using var packageStream = File.Open(packageFileName, System.IO.FileMode.Open, FileAccess.Read, FileShare.Read);
					using var package = new PackageArchiveReader(packageStream);
					/*
					Workaround to get all available metadata from a NuGet package, even the metadata is not
					exposed by NuGet.Packaging.NuspecReader. For example, NuspecReader version 4.3.0 does
					not expose the "repository" metadata.
					*/
					var metadataNode = package.NuspecReader.Xml.Root.Elements()
						.Single(e => e.Name.LocalName.Equals("metadata", StringComparison.Ordinal));
					var rawNugetMetadata = metadataNode.Elements()
						.ToDictionary(
							e => e.Name.LocalName,
							e => (e.Value, (IDictionary<string, string>)e.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value)));

					var license = package.NuspecReader.GetMetadataValue("license");
					var iconUrl = package.NuspecReader.GetIconUrl();
					var projectUrl = package.NuspecReader.GetProjectUrl();
					var packageVersion = package.NuspecReader.GetVersion().ToNormalizedString();

					// Only get TFM for lib folder. If there are other TFM used for other folders (tool, content, build, ...) we're not interested in it.
					// Also filter out the "any" platform which is used when the platform is unknown
					var frameworks = package
						.GetLibItems()
						.Select(i => i.TargetFramework.GetShortFolderName())
						.Except(new[] { "any" })
						.ToArray();

					var normalizedPackageDependencies = package.GetPackageDependencies()
						.SelectMany(d => d.Packages)
						.Select(d => new
						{
							d.Id,
							NuGetVersion = (d.VersionRange.HasUpperBound ? d.VersionRange.MaxVersion : d.VersionRange.MinVersion) ?? new NuGetVersion("0.0.0")
						})
						.ToArray();

					var hasPreReleaseDependencies = normalizedPackageDependencies.Any(d => d.NuGetVersion.IsPrerelease);

					var packageDependencies = normalizedPackageDependencies
						.Select(d => new DllReference()
						{
							Id = d.Id,
							IsPrivate = false,
							Version = new SemVersion(d.NuGetVersion.Version)
						})
						.ToArray();

#pragma warning disable SA1009 // Closing parenthesis should be spaced correctly
					var assembliesPath = package.GetFiles()
						.Where(f =>
							Path.GetExtension(f).EqualsIgnoreCase(".dll") &&
							!Path.GetFileNameWithoutExtension(f).EqualsIgnoreCase("Cake.Core") &&
							!Path.GetFileNameWithoutExtension(f).EqualsIgnoreCase("Cake.Common") &&
							(
								Path.GetFileName(f).EqualsIgnoreCase($"{addin.Name}.dll") ||
								string.IsNullOrEmpty(Path.GetDirectoryName(f)) ||
								f.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) ||
								f.StartsWith("lib/", StringComparison.OrdinalIgnoreCase)
							))
						.OrderByDescending(f =>
							f.Contains("net8", StringComparison.OrdinalIgnoreCase) ? 7 :
							f.Contains("net7", StringComparison.OrdinalIgnoreCase) ? 6 :
							f.Contains("net6", StringComparison.OrdinalIgnoreCase) ? 5 :
							f.Contains("net5", StringComparison.OrdinalIgnoreCase) ? 4 :
							f.Contains("netstandard2", StringComparison.OrdinalIgnoreCase) ? 3 :
							f.Contains("netstandard1", StringComparison.OrdinalIgnoreCase) ? 2 :
							f.Contains("net4", StringComparison.OrdinalIgnoreCase) ? 1 :
							0)
						.ThenByDescending(f =>
							Path.GetFileName(f).EqualsIgnoreCase($"{addin.Name}.dll") ? 2 :
							Path.GetFileName(f).StartsWithIgnoreCase("Cake.") ? 1 :
							0)
						.ToArray();
#pragma warning restore SA1009 // Closing parenthesis should be spaced correctly

					//--------------------------------------------------
					// Find the first DLL that contains Cake alias attributes (i.e.: 'CakePropertyAlias' or 'CakeMethodAlias')
					var assemblyInfoToAnalyze = FindAssemblyToAnalyze(package, assembliesPath);

					//--------------------------------------------------
					// Determine the type of the nuget package
					if (assembliesPath.Length == 0)
					{
						// This package does not contain DLLs. We'll assume it contains "recipes" .cake files.
						addin.Type = AddinType.Recipe;
					}
					else if (addin.Name.EndsWith(".Module", StringComparison.OrdinalIgnoreCase))
					{
						addin.Type = AddinType.Module;
					}
					else if (assemblyInfoToAnalyze.DecoratedMethods.Any())
					{
						addin.Type = AddinType.Addin;
						addin.DecoratedMethods = assemblyInfoToAnalyze.DecoratedMethods;
					}
					else
					{
						addin.Type = AddinType.Unknown;
					}

					//--------------------------------------------------
					// Check if the symbols are available.
					// If so, check if SourceLink is enabled.
					addin.PdbStatus = PdbStatus.NotAvailable;
					addin.SourceLinkEnabled = false;

					// First, check if the PDB is included in the nupkg
					try
					{
						var pdbFileInNupkg = package.GetFiles()
							.FirstOrDefault(f =>
								Path.GetExtension(f).EqualsIgnoreCase(".pdb") &&
								Path.GetFileNameWithoutExtension(f).EqualsIgnoreCase(addin.Name));

						if (!string.IsNullOrEmpty(pdbFileInNupkg))
						{
							addin.PdbStatus = PdbStatus.IncludedInPackage;

							var pdbStream = package.GetStream(pdbFileInNupkg);
							addin.SourceLinkEnabled = HasSourceLinkDebugInformation(pdbStream);
						}
					}
					catch
					{
						// Ignore exceptions
					}

					// Secondly, check if symbols are embedded in the DLL
					if (addin.PdbStatus == PdbStatus.NotAvailable && assemblyInfoToAnalyze.AssemblyStream != null)
					{
						try
						{
							assemblyInfoToAnalyze.AssemblyStream.Position = 0;
							var peReader = new PEReader(assemblyInfoToAnalyze.AssemblyStream);

							if (peReader.ReadDebugDirectory().Any(de => de.Type == DebugDirectoryEntryType.EmbeddedPortablePdb))
							{
								addin.PdbStatus = PdbStatus.Embedded;

								var embeddedEntry = peReader.ReadDebugDirectory().First(de => de.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);
								using var embeddedMetadataProvider = peReader.ReadEmbeddedPortablePdbDebugDirectoryData(embeddedEntry);
								var pdbReader = embeddedMetadataProvider.GetMetadataReader();
								addin.SourceLinkEnabled = HasSourceLinkDebugInformation(pdbReader);
							}
						}
						catch
						{
							// Ignore exceptions
						}
					}

					// Finally, check if the PDB is included in the snupkg
					if (addin.PdbStatus == PdbStatus.NotAvailable)
					{
						try
						{
							var symbolsFileName = Path.Combine(context.PackagesFolder, $"{addin.Name}.{addin.NuGetPackageVersion}.snupkg");
							if (File.Exists(symbolsFileName))
							{
								using var symbolsStream = File.Open(symbolsFileName, System.IO.FileMode.Open, FileAccess.Read, FileShare.Read);
								using var symbolsPackage = new PackageArchiveReader(symbolsStream);

								var pdbFileInSnupkg = symbolsPackage.GetFiles()
									.FirstOrDefault(f =>
										Path.GetExtension(f).EqualsIgnoreCase(".pdb") &&
										Path.GetFileNameWithoutExtension(f).EqualsIgnoreCase(addin.Name));

								if (!string.IsNullOrEmpty(pdbFileInSnupkg))
								{
									addin.PdbStatus = PdbStatus.IncludedInSymbolsPackage;

									var pdbStream = package.GetStream(pdbFileInSnupkg);
									addin.SourceLinkEnabled = HasSourceLinkDebugInformation(pdbStream);
								}
							}
						}
						catch
						{
							// Ignore exceptions
						}
					}

					//--------------------------------------------------
					// Find the XML documentation
					var assemblyFolder = Path.GetDirectoryName(assemblyInfoToAnalyze.AssemblyPath);
					var xmlDocumentation = package.GetFiles()
						.FirstOrDefault(f =>
							Path.GetExtension(f).EqualsIgnoreCase(".xml") &&
							Path.GetFileNameWithoutExtension(f).EqualsIgnoreCase(addin.Name) &&
							Path.GetDirectoryName(f).EqualsIgnoreCase(assemblyFolder));

					addin.XmlDocumentationAvailable = !string.IsNullOrEmpty(xmlDocumentation);

					//--------------------------------------------------
					// Find the DLL references
					var dllReferences = Array.Empty<DllReference>();
					if (assemblyInfoToAnalyze.Assembly != null)
					{
						var assemblyReferences = assemblyInfoToAnalyze.Assembly
							.GetReferencedAssemblies()
							.Select(r => new DllReference()
							{
								Id = r.Name,
								IsPrivate = true,
								Version = new SemVersion(r.Version)
							})
							.ToArray();

						dllReferences = packageDependencies.Union(assemblyReferences)
							.GroupBy(d => d.Id)
							.Select(grp => new DllReference()
							{
								Id = grp.Key,
								IsPrivate = grp.All(r => r.IsPrivate),
								Version = grp.Min(r => r.Version)
							})
							.ToArray();
					}

					// Get the cake-version.yml (if present)
					var yamlExtensions = new[] { ".yml", ".yaml" };
					var cakeVersionYamlFilePath = package.GetFiles()
						.FirstOrDefault(f =>
							Array.Exists(yamlExtensions, e => e.EqualsIgnoreCase(Path.GetExtension(f))) &&
							Path.GetFileNameWithoutExtension(f).EqualsIgnoreCase("cake-version"));
					if (!string.IsNullOrEmpty(cakeVersionYamlFilePath))
					{
						using var cakeVersionYamlFileStream = LoadFileFromPackage(package, cakeVersionYamlFilePath);
						using TextReader ymlReader = new StreamReader(cakeVersionYamlFileStream);

						var deserializer = new YamlDotNet.Serialization.Deserializer();
						addin.CakeVersionYaml = deserializer.Deserialize<CakeVersionYamlConfig>(ymlReader);
					}

					addin.License = license;
					addin.IconUrl = string.IsNullOrEmpty(iconUrl) ? null : new Uri(iconUrl);
					addin.NuGetPackageVersion = packageVersion;
					addin.Frameworks = frameworks;
					addin.References = dllReferences;
					addin.HasPrereleaseDependencies = hasPreReleaseDependencies;
					addin.DllName = string.IsNullOrEmpty(assemblyInfoToAnalyze.AssemblyPath) ? string.Empty : Path.GetFileName(assemblyInfoToAnalyze.AssemblyPath);
					addin.AliasCategories = assemblyInfoToAnalyze.AliasCategories;

					rawNugetMetadata.TryGetValue("repository", out (string Value, IDictionary<string, string> Attributes) repositoryInfo);
					if (repositoryInfo != default && repositoryInfo.Attributes.TryGetValue("url", out string repoUrl))
					{
						addin.RepositoryUrl = new Uri(repoUrl);
					}

					rawNugetMetadata.TryGetValue("icon", out (string Value, IDictionary<string, string> Attributes) iconInfo);
					if (iconInfo != default)
					{
						try
						{
							using var iconFileContent = LoadFileFromPackage(package, iconInfo.Value);
							addin.EmbeddedIcon = iconFileContent.ToArray();
						}
						catch
						{
							throw new Exception($"Unable to find {iconInfo.Value} in the package");
						}
					}
				}

				if (addin.Type == AddinType.Unknown)
				{
					throw new Exception($"This addin does not contain any decorated method.{Environment.NewLine}");
				}
			}
			catch (Exception e)
			{
				addin.AnalysisResult.Notes += $"AnalyzeNugetMetadata: {e.GetBaseException().Message}{Environment.NewLine}";
			}
		}

		private static MemoryStream LoadFileFromPackage(IPackageCoreReader package, string filePath)
		{
			try
			{
				var cleanPath = filePath.Replace('/', '\\');
				if (cleanPath.IndexOf('%') > -1)
				{
					cleanPath = Uri.UnescapeDataString(cleanPath);
				}

				using var fileStream = package.GetStream(cleanPath);
				var decompressedStream = new MemoryStream();
				fileStream.CopyTo(decompressedStream);
				decompressedStream.Position = 0;
				return decompressedStream;
			}
			catch (FileLoadException e)
			{
				// Note: intentionally discarding the original exception because I want to ensure the following message is displayed in the 'Exceptions' report
				throw new FileLoadException($"An error occured while loading {Path.GetFileName(filePath)} from the Nuget package. {e.Message}");
			}
		}

		private static bool HasSourceLinkDebugInformation(Stream pdbStream)
		{
			if (!pdbStream.CanSeek)
			{
				using var seekablePdbStream = new MemoryStream();
				pdbStream.CopyTo(seekablePdbStream);
				seekablePdbStream.Position = 0;

				return HasSourceLinkDebugInformation(seekablePdbStream);
			}

			using var pdbReaderProvider = MetadataReaderProvider.FromPortablePdbStream(pdbStream);
			var pdbReader = pdbReaderProvider.GetMetadataReader();

			return HasSourceLinkDebugInformation(pdbReader);
		}

		private static bool HasSourceLinkDebugInformation(MetadataReader pdbReader)
		{
			foreach (var customDebugInfoHandle in pdbReader.CustomDebugInformation)
			{
				var customDebugInfo = pdbReader.GetCustomDebugInformation(customDebugInfoHandle);
				if (pdbReader.GetGuid(customDebugInfo.Kind) == SourceLinkCustomDebugInfoGuid)
				{
					// var sourceLinkContent = pdbReader.GetBlobBytes(customDebugInfo.Value);
					// var sourceLinkText = System.Text.Encoding.UTF8.GetString(sourceLinkContent);
					return true;
				}
			}

			return false;
		}

		private static (Stream AssemblyStream, Assembly Assembly, MethodInfo[] DecoratedMethods, string AssemblyPath, string[] AliasCategories) FindAssemblyToAnalyze(IPackageCoreReader package, string[] assembliesPath)
		{
			foreach (var assemblyPath in assembliesPath)
			{
				var runtimeAssemblies = Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll");

				// The assembly resolver makes the assemblies referenced by THIS APPLICATION (i.e.: the AddinDiscoverer) available
				// for resolving types when looping through custom attributes. As of this writing, there is one addin written in
				// FSharp which was causing 'Could not find FSharp.Core' when looping through its custom attributes. To solve this
				// problem, I added a reference to FSharp.Core in Cake.AddinDiscoverer.csproj
				var assemblyResolver = new PathAssemblyResolver(runtimeAssemblies);

				// It's important to create a new load context for each assembly to ensure one addin does not interfere with another
				var loadContext = new MetadataLoadContext(assemblyResolver);

				// Load the assembly
				var assemblyStream = LoadFileFromPackage(package, assemblyPath);
				var assembly = loadContext.LoadFromStream(assemblyStream);

				var isModule = assembly
					.CustomAttributes
					.Any(a => a.AttributeType.Namespace == "Cake.Core.Annotations" && a.AttributeType.Name == "CakeModuleAttribute");

				// Search for decorated methods.
				// Please note that it's important to use the '.ExportedTypes' and the '.CustomAttributes' properties rather than
				// the '.GetExportedTypes' and the '.GetCustomAttributes' methods because the latter will instantiate the custom
				// attributes which, in our case, will cause exceptions because they are defined in dependencies which are most
				// likely not available in the load context.
				var decoratedMethods = assembly
					.ExportedTypes
					.SelectMany(type => type.GetTypeInfo().DeclaredMethods)
					.Where(method =>
						method.CustomAttributes.Any(a =>
							a.AttributeType.Namespace == "Cake.Core.Annotations" &&
							(a.AttributeType.Name == "CakeMethodAliasAttribute" || a.AttributeType.Name == "CakePropertyAliasAttribute")))
					.ToArray();

				// Search for alias categories
				var aliasCategories = assembly
					.ExportedTypes
					.SelectMany(t => t.CustomAttributes)
					.Where(a => a.AttributeType.Namespace == "Cake.Core.Annotations" && a.AttributeType.Name == "CakeAliasCategoryAttribute")
					.Select(a => a.ConstructorArguments[0].Value?.ToString())
					.Where(category => !string.IsNullOrEmpty(category))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToArray();

				if (isModule || decoratedMethods.Any())
				{
					return (assemblyStream, assembly, decoratedMethods, assemblyPath, aliasCategories);
				}
			}

			return (null, null, Array.Empty<MethodInfo>(), string.Empty, Array.Empty<string>());
		}

		private static async Task ValidateUrls(DiscoveryContext context, AddinMetadata addin, IReadOnlyList<Octokit.Repository> cakeContribRepositories)
		{
			// Some addins were moved to the cake-contrib organization but the URL in their package metadata still
			// points to the original repo. This step corrects the URL to ensure it points to the right repo
			var repo = cakeContribRepositories.FirstOrDefault(r => r.Name.EqualsIgnoreCase(addin.Name));
			if (repo != null)
			{
				// Only overwrite GitHub and Bitbucket URLs and preserve custom URLs such as 'https://cakeissues.net/' for example.
				if (addin.ProjectUrl.IsGithubUrl(false) || addin.ProjectUrl.IsBitbucketUrl())
				{
					addin.ProjectUrl = new Uri(repo.HtmlUrl);
				}

				addin.InferredRepositoryUrl = new Uri(repo.CloneUrl);
			}

			// Derive the repository name and owner
			var ownershipDerived = Misc.DeriveGitHubRepositoryInfo(addin.InferredRepositoryUrl ?? addin.RepositoryUrl ?? addin.ProjectUrl, out string repoOwner, out string repoName);
			if (ownershipDerived)
			{
				addin.RepositoryOwner = repoOwner;
				addin.RepositoryName = repoName;
			}

			// Validate GitHub URL
			if (repo == null && ownershipDerived)
			{
				try
				{
					var repository = await context.GithubClient.Repository.Get(repoOwner, repoName).ConfigureAwait(false);

					// Only overwrite GitHub and Bitbucket URLs and preserve custom URLs such as 'https://cakeissues.net/' for example.
					if (addin.ProjectUrl.IsGithubUrl(false) || addin.ProjectUrl.IsBitbucketUrl())
					{
						addin.ProjectUrl = new Uri(repository.HtmlUrl);
					}

					addin.InferredRepositoryUrl = new Uri(repository.CloneUrl);

					// Derive the repository name and owner with the new repo URL
					if (Misc.DeriveGitHubRepositoryInfo(addin.InferredRepositoryUrl, out repoOwner, out repoName))
					{
						addin.RepositoryOwner = repoOwner;
						addin.RepositoryName = repoName;
					}
				}
				catch (NotFoundException)
				{
					addin.ProjectUrl = null;
				}
#pragma warning disable CS0168 // Variable is declared but never used
				catch (Exception e)
#pragma warning restore CS0168 // Variable is declared but never used
				{
					throw;
				}
			}

			// Validate non-GitHub URL
			if (addin.ProjectUrl != null && !addin.ProjectUrl.IsGithubUrl(false))
			{
				var githubRequest = new Request()
				{
					BaseAddress = new UriBuilder(addin.ProjectUrl.Scheme, addin.ProjectUrl.Host, addin.ProjectUrl.Port).Uri,
					Endpoint = new Uri(addin.ProjectUrl.PathAndQuery, UriKind.Relative),
					Method = HttpMethod.Head,
				};
				githubRequest.Headers.Add("User-Agent", ((Connection)context.GithubClient.Connection).UserAgent);

				var response = await SendRequestWithRetries(githubRequest, context.GithubHttpClient).ConfigureAwait(false);

				if (response.StatusCode == HttpStatusCode.NotFound)
				{
					addin.ProjectUrl = null;
				}
			}

			// Standardize GitHub URLs
			addin.InferredRepositoryUrl = Misc.StandardizeGitHubUri(addin.InferredRepositoryUrl);
			addin.RepositoryUrl = Misc.StandardizeGitHubUri(addin.RepositoryUrl);
			addin.ProjectUrl = Misc.StandardizeGitHubUri(addin.ProjectUrl);
		}

		private static async Task<IResponse> SendRequestWithRetries(IRequest request, IHttpClient httpClient)
		{
			IResponse response = null;
			const int maxRetry = 3;
			for (int retryCount = 0; retryCount < maxRetry; retryCount++)
			{
				response = await httpClient.Send(request, CancellationToken.None).ConfigureAwait(false);

				if (response.StatusCode == HttpStatusCode.TooManyRequests && retryCount < maxRetry - 1)
				{
					response.Headers.TryGetValue("Retry-After", out string retryAfter);
					await Task.Delay(1000 * int.Parse(retryAfter ?? "60")).ConfigureAwait(false);
				}
				else
				{
					break;
				}
			}

			return response;
		}

		private static void AnalyzeAddin(DiscoveryContext context, AddinMetadata addin, byte[] cakeContribIcon, KeyValuePair<AddinType, byte[]>[] fancyIcons)
		{
			addin.AnalysisResult.AtLeastOneDecoratedMethod = addin.DecoratedMethods?.Any() ?? false;

			if (addin.References != null)
			{
				var cakeCommonReference = addin.References.Where(r => r.Id.EqualsIgnoreCase("Cake.Common"));
				if (cakeCommonReference.Any())
				{
					var cakeCommonVersion = cakeCommonReference.Min(r => r.Version);
					var cakeCommonIsPrivate = cakeCommonReference.All(r => r.IsPrivate);
					addin.AnalysisResult.CakeCommonVersion = cakeCommonVersion ?? Constants.VERSION_UNKNOWN;
					addin.AnalysisResult.CakeCommonIsPrivate = cakeCommonIsPrivate;
				}
				else
				{
					addin.AnalysisResult.CakeCommonVersion = null;
					addin.AnalysisResult.CakeCommonIsPrivate = true;
				}

				var cakeCoreReference = addin.References.Where(r => r.Id.EqualsIgnoreCase("Cake.Core"));
				if (cakeCoreReference.Any())
				{
					var cakeCoreVersion = cakeCoreReference.Min(r => r.Version);
					var cakeCoreIsPrivate = cakeCoreReference.All(r => r.IsPrivate);
					addin.AnalysisResult.CakeCoreVersion = cakeCoreVersion ?? Constants.VERSION_UNKNOWN;
					addin.AnalysisResult.CakeCoreIsPrivate = cakeCoreIsPrivate;
				}
				else
				{
					addin.AnalysisResult.CakeCoreVersion = null;
					addin.AnalysisResult.CakeCoreIsPrivate = true;
				}
			}

			if (addin.Type == AddinType.Addin && addin.AnalysisResult.CakeCoreVersion == null && addin.AnalysisResult.CakeCommonVersion == null)
			{
				addin.AnalysisResult.Notes += $"This addin seems to be referencing neither Cake.Core nor Cake.Common.{Environment.NewLine}";
			}

			addin.AnalysisResult.Icon = AnalyzeIcon(addin, cakeContribIcon, fancyIcons);
			addin.AnalysisResult.TransferredToCakeContribOrganization = addin.RepositoryOwner?.Equals(Constants.CAKE_CONTRIB_REPO_OWNER, StringComparison.OrdinalIgnoreCase) ?? false;
			addin.AnalysisResult.ObsoleteLicenseUrlRemoved = !string.IsNullOrEmpty(addin.License);
			addin.AnalysisResult.RepositoryInfoProvided = addin.RepositoryUrl != null;
			addin.AnalysisResult.PackageCoOwnedByCakeContrib = addin.NuGetPackageOwners.Contains("cake-contrib", StringComparer.OrdinalIgnoreCase);
		}

		private static IconAnalysisResult AnalyzeIcon(AddinMetadata addin, byte[] recommendedIcon, IEnumerable<KeyValuePair<AddinType, byte[]>> fancyIcons)
		{
			if (addin.EmbeddedIcon != null) return AnalyzeEmbeddedIcon(addin.EmbeddedIcon, addin.Type, recommendedIcon, fancyIcons);
			else if (addin.IconUrl != null) return AnalyzeLinkedIcon(addin.IconUrl);
			else return IconAnalysisResult.Unspecified;
		}

		private static IconAnalysisResult AnalyzeEmbeddedIcon(byte[] embeddedIcon, AddinType addinType, byte[] recommendedIcon, IEnumerable<KeyValuePair<AddinType, byte[]>> fancyIcons)
		{
			// Check if the icon matches the recommended Cake-Contrib icon
			if (Misc.ByteArrayCompare(embeddedIcon, recommendedIcon))
			{
				return IconAnalysisResult.EmbeddedCakeContrib;
			}

			// Check if the icon matches one of the "fancy" icons of the appropriate type
			foreach (var fancyIcon in fancyIcons)
			{
				if (fancyIcon.Key.IsFlagSet(addinType) && Misc.ByteArrayCompare(embeddedIcon, fancyIcon.Value))
				{
					return IconAnalysisResult.EmbeddedFancyCakeContrib;
				}
			}

			// The icon doesn't match any of the recommended icons
			return IconAnalysisResult.EmbeddedCustom;
		}

		private static IconAnalysisResult AnalyzeLinkedIcon(Uri iconUrl)
		{
			if (iconUrl.AbsoluteUri.EqualsIgnoreCase(Constants.OLD_CAKE_CONTRIB_ICON_URL)) return IconAnalysisResult.RawgitUrl;
			if (iconUrl.AbsoluteUri.EqualsIgnoreCase(Constants.NEW_CAKE_CONTRIB_ICON_URL)) return IconAnalysisResult.JsDelivrUrl;
			return IconAnalysisResult.CustomUrl;
		}

		private static void DeterminePackageOwners(IDictionary<string, string[]> allOwners, AddinMetadata addin)
		{
			if (addin.NuGetPackageOwners == Array.Empty<string>())
			{
				if (allOwners.TryGetValue(addin.Name, out string[] owners))
				{
					addin.NuGetPackageOwners = owners;
				}
			}
		}

		private static async Task CheckUsingCakeRecipe(DiscoveryContext context, AddinMetadata addin, SemVersion latestCakeRecipeVersion)
		{
			if (!string.IsNullOrEmpty(addin.RepositoryName) && !string.IsNullOrEmpty(addin.RepositoryOwner))
			{
				try
				{
					// Get all files from the repo
					var repoContent = await Misc.GetRepoContentAsync(context, addin.RepositoryOwner, addin.RepositoryName).ConfigureAwait(false);

					// Get the cake files
					var repoItems = repoContent
						.Where(item => Path.GetExtension(item.Key) == ".cake")
						.ToArray();

					foreach (var repoItem in repoItems)
					{
						// Get the content of the cake file
						repoItem.Value.Position = 0;
						var cakeFileContent = await new StreamReader(repoItem.Value).ReadToEndAsync().ConfigureAwait(false);

						if (!string.IsNullOrEmpty(cakeFileContent))
						{
							// Parse the cake file
							var recipeFile = new RecipeFile()
							{
								Content = cakeFileContent
							};

							// Check if this file references Cake.Recipe
							var cakeRecipeReference = recipeFile.LoadReferences.FirstOrDefault(r => r.Name.EqualsIgnoreCase("Cake.Recipe"));
							if (cakeRecipeReference != null)
							{
								addin.AnalysisResult.CakeRecipeIsUsed = true;
								addin.AnalysisResult.CakeRecipeVersion = cakeRecipeReference.ReferencedVersion;
								addin.AnalysisResult.CakeRecipeIsPrerelease = cakeRecipeReference.Prerelease;
								addin.AnalysisResult.CakeRecipeIsLatest = cakeRecipeReference.ReferencedVersion == null || cakeRecipeReference.ReferencedVersion == latestCakeRecipeVersion;
							}
						}

						if (addin.AnalysisResult.CakeRecipeIsUsed) break;
					}
				}
				catch (ApiException e) when (e.StatusCode == HttpStatusCode.NotFound)
				{
					// I know of at least one case where the URL in the NuGet metadata points to a repo that has been deleted.
					// Therefore it's safe to ignore this error.
				}
				finally
				{
					// This is to ensure we don't issue requests too quickly and therefore trigger Github's abuse detection
					await Misc.RandomGithubDelayAsync().ConfigureAwait(false);
				}
			}
		}
	}
}
