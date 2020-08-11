using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class AnalyzeNuGetMetadataStep : IStep
	{
		// https://github.com/dotnet/roslyn/blob/b3cbe7abce7633e45d7dd468bde96bfe24ccde47/src/Dependencies/CodeAnalysis.Debugging/PortableCustomDebugInfoKinds.cs#L18
		private static readonly Guid SourceLinkCustomDebugInfoGuid = new Guid("CC110556-A091-4D38-9FEC-25AB9A351A6A");

		public bool PreConditionIsMet(DiscoveryContext context) => true;

		public string GetDescription(DiscoveryContext context) => "Analyze nuget packages metadata";

		public async Task ExecuteAsync(DiscoveryContext context)
		{
			context.Addins = context.Addins
				.Select(addin =>
				{
					try
					{
						var packageFileName = Path.Combine(context.PackagesFolder, $"{addin.Name}.{addin.NuGetPackageVersion}.nupkg");
						if (File.Exists(packageFileName))
						{
							using var packageStream = File.Open(packageFileName, System.IO.FileMode.Open, FileAccess.Read, FileShare.Read);
							using var package = new PackageArchiveReader(packageStream);
							/*
							Workaround to get all available metadata from a NuGet package, even the metadata not
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
							var frameworks = package.GetSupportedFrameworks().Select(f => f.GetShortFolderName()).ToArray();

							var normalizedPackageDependencies = package.GetPackageDependencies()
								.SelectMany(d => d.Packages)
								.Select(d => new
								{
									d.Id,
									NuGetVersion = d.VersionRange.HasUpperBound ? d.VersionRange.MaxVersion : d.VersionRange.MinVersion
								});

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
									f.Contains("netstandard2", StringComparison.OrdinalIgnoreCase) ? 3 :
									f.Contains("netstandard1", StringComparison.OrdinalIgnoreCase) ? 2 :
									f.Contains("net4", StringComparison.OrdinalIgnoreCase) ? 1 :
									0)
								.ToArray();
#pragma warning restore SA1009 // Closing parenthesis should be spaced correctly

							//--------------------------------------------------
							// Find the DLL that matches the naming convention
							var assemblyPath = assembliesPath.FirstOrDefault(f => Path.GetFileName(f).EqualsIgnoreCase($"{addin.Name}.dll"));
							if (string.IsNullOrEmpty(assemblyPath))
							{
								// This package does not contain DLLs. We'll assume it contains "recipes" .cake files.
								if (assembliesPath.Length == 0)
								{
									addin.Type = AddinType.Recipe;
								}

								// If a package contains only one DLL, we will analyze this DLL even if it doesn't match the expected naming convention
								else if (assembliesPath.Length == 1)
								{
									assemblyPath = assembliesPath.First();
								}
							}

							//--------------------------------------------------
							// Load the assembly
							var loadContext = new AssemblyLoaderContext();
							var assemblyStream = (Stream)null;
							var assembly = (Assembly)null;

							if (!string.IsNullOrEmpty(assemblyPath))
							{
								assemblyStream = LoadFileFromPackage(package, assemblyPath);
								assembly = loadContext.LoadFromStream(assemblyStream);
							}

							//--------------------------------------------------
							// Check if the symbols are available.
							// If so, check if SourceLink is enabled.
							addin.PdbStatus = PdbStatus.NotAvailable;
							addin.SourceLinkEnabled = false;

							// First, check if the PDB is included in the nupkg
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

							// Secondly, check if symbols are embedded in the DLL
							if (addin.PdbStatus == PdbStatus.NotAvailable && assemblyStream != null)
							{
								assemblyStream.Position = 0;
								var peReader = new PEReader(assemblyStream);

								if (peReader.ReadDebugDirectory().Any(de => de.Type == DebugDirectoryEntryType.EmbeddedPortablePdb))
								{
									addin.PdbStatus = PdbStatus.Embedded;

									var embeddedEntry = peReader.ReadDebugDirectory().First(de => de.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);
									using var embeddedMetadataProvider = peReader.ReadEmbeddedPortablePdbDebugDirectoryData(embeddedEntry);
									var pdbReader = embeddedMetadataProvider.GetMetadataReader();
									addin.SourceLinkEnabled = HasSourceLinkDebugInformation(pdbReader);
								}
							}

							// Finally, check if the PDB is included in the snupkg
							if (addin.PdbStatus == PdbStatus.NotAvailable)
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

							//--------------------------------------------------
							// Find the DLL references
							var dllReferences = Array.Empty<DllReference>();
							if (assembly != null)
							{
								var assemblyReferences = assembly
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

							addin.License = license;
							addin.IconUrl = string.IsNullOrEmpty(iconUrl) ? null : new Uri(iconUrl);
							addin.NuGetPackageVersion = packageVersion;
							addin.Frameworks = frameworks;
							addin.References = dllReferences;
							addin.HasPrereleaseDependencies = hasPreReleaseDependencies;

							if (addin.Name.EndsWith(".Module", StringComparison.OrdinalIgnoreCase)) addin.Type = AddinType.Module;
							if (addin.Type == AddinType.Unknown && !string.IsNullOrEmpty(assemblyPath)) addin.Type = AddinType.Addin;
							if (!string.IsNullOrEmpty(assemblyPath)) addin.DllName = Path.GetFileName(assemblyPath);

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
							throw new Exception("We are unable to determine the type of this addin. One likely reason is that it contains multiple DLLs but none of them respect the naming convention.");
						}
					}
					catch (Exception e)
					{
						addin.AnalysisResult.Notes += $"AnalyzeNugetMetadata: {e.GetBaseException().Message}{Environment.NewLine}";
					}

					return addin;
				})
				.ToArray();

			await Task.Delay(1).ConfigureAwait(false);
		}

		private static Assembly LoadAssemblyFromPackage(IPackageCoreReader package, string assemblyPath, AssemblyLoadContext loadContext)
		{
			using var assemblyStream = LoadFileFromPackage(package, assemblyPath);
			return loadContext.LoadFromStream(LoadFileFromPackage(package, assemblyPath));
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

		private bool HasSourceLinkDebugInformation(Stream pdbStream)
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

		private bool HasSourceLinkDebugInformation(MetadataReader pdbReader)
		{
			foreach (var customDebugInfoHandle in pdbReader.CustomDebugInformation)
			{
				var customDebugInfo = pdbReader.GetCustomDebugInformation(customDebugInfoHandle);
				if (pdbReader.GetGuid(customDebugInfo.Kind) == SourceLinkCustomDebugInfoGuid)
				{
					//var sourceLinkContent = pdbReader.GetBlobBytes(customDebugInfo.Value);
					//var sourceLinkText = System.Text.Encoding.UTF8.GetString(sourceLinkContent);

					return true;
				}
			}

			return false;
		}
	}
}
