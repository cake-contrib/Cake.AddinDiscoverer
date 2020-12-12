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
using System.Runtime.InteropServices;
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

					return addin;
				})
				.ToArray();

			await Task.Delay(1).ConfigureAwait(false);
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
					// var sourceLinkContent = pdbReader.GetBlobBytes(customDebugInfo.Value);
					// var sourceLinkText = System.Text.Encoding.UTF8.GetString(sourceLinkContent);
					return true;
				}
			}

			return false;
		}

		private (Stream AssemblyStream, Assembly Assembly, MethodInfo[] DecoratedMethods, string AssemblyPath, string[] AliasCategories) FindAssemblyToAnalyze(IPackageCoreReader package, string[] assembliesPath)
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

				if (decoratedMethods.Any())
				{
					return (assemblyStream, assembly, decoratedMethods, assemblyPath, aliasCategories);
				}
			}

			return (null, null, Array.Empty<MethodInfo>(), string.Empty, Array.Empty<string>());
		}
	}
}
