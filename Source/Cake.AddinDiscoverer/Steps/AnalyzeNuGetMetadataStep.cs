using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class AnalyzeNuGetMetadataStep : IStep
	{
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
							using (var stream = File.Open(packageFileName, System.IO.FileMode.Open, FileAccess.Read, FileShare.Read))
							{
								using (var package = new PackageArchiveReader(stream))
								{
									var iconUrl = package.NuspecReader.GetIconUrl();
									var projectUrl = package.NuspecReader.GetProjectUrl();
									var packageVersion = package.NuspecReader.GetVersion().ToNormalizedString();
									var frameworks = package.GetSupportedFrameworks().Select(f =>
									{
										if (f.Framework.EqualsIgnoreCase(".NETStandard"))
										{
											return $"netstandard{f.Version.Major}.{f.Version.Minor}";
										}
										else if (f.Framework.EqualsIgnoreCase(".NETCore"))
										{
											return $"netcoreapp{f.Version.Major}.{f.Version.Minor}";
										}
										else if (f.Framework.EqualsIgnoreCase(".NETFramework"))
										{
											if (f.Version.Revision == 0)
											{
												return $"net{f.Version.Major}{f.Version.Minor}";
											}
											else
											{
												return $"net{f.Version.Major}{f.Version.Minor}{f.Version.Revision}";
											}
										}
										else
										{
											return f.GetFrameworkString();
										}
									}).ToArray();

									var normalizedPackageDependencies = package.GetPackageDependencies()
										.SelectMany(d => d.Packages)
										.Select(d =>
										{
											return new
											{
												d.Id,
												NuGetVersion = d.VersionRange.HasUpperBound ? d.VersionRange.MaxVersion : d.VersionRange.MinVersion
											};
										});

									var isPreRelease = normalizedPackageDependencies.Any(d => d.NuGetVersion.IsPrerelease);

									var packageDependencies = normalizedPackageDependencies
										.Select(d =>
										{
											return new DllReference()
											{
												Id = d.Id,
												IsPrivate = false,
												Version = new SemVersion(d.NuGetVersion.Version)
											};
										})
										.ToArray();

#pragma warning disable SA1009 // Closing parenthesis should be spaced correctly
									var assembliesPath = package.GetFiles()
												.Where(f =>
												{
													return Path.GetExtension(f).EqualsIgnoreCase(".dll") &&
															!Path.GetFileNameWithoutExtension(f).EqualsIgnoreCase("Cake.Core") &&
															!Path.GetFileNameWithoutExtension(f).EqualsIgnoreCase("Cake.Common") &&
															(
																Path.GetFileName(f).EqualsIgnoreCase($"{addin.Name}.dll") ||
																string.IsNullOrEmpty(Path.GetDirectoryName(f)) ||
																f.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) ||
																f.StartsWith("lib/", StringComparison.OrdinalIgnoreCase)
															);
												})
												.OrderByDescending(f =>
													f.Contains("netstandard2", StringComparison.OrdinalIgnoreCase) ? 3 :
													f.Contains("netstandard1", StringComparison.OrdinalIgnoreCase) ? 2 :
													f.Contains("net4", StringComparison.OrdinalIgnoreCase) ? 1 :
													0)
												.ToArray();
#pragma warning restore SA1009 // Closing parenthesis should be spaced correctly

									// Find the DLL that matches the naming convention
									var assemblyPath = assembliesPath.FirstOrDefault(f => Path.GetFileName(f).EqualsIgnoreCase($"{addin.Name}.dll"));
									if (string.IsNullOrEmpty(assemblyPath))
									{
										// This package does not contain DLLs. We'll assume it contains "recipes" .cake files.
										if (assembliesPath.Length == 0)
										{
											addin.Type = AddinType.Recipes;
										}

										// If a package contains only one DLL, we will analyze this DLL even if it doesn't match the expected naming convention
										else if (assembliesPath.Length == 1)
										{
											assemblyPath = assembliesPath.First();
										}
									}

									// Find the DLL references
									var dllReferences = Array.Empty<DllReference>();
									if (!string.IsNullOrEmpty(assemblyPath))
									{
										var loadContext = new AssemblyLoaderContext();
										var assembly = LoadAssemblyFromPackage(package, assemblyPath, loadContext);
										var assemblyReferences = assembly
											.GetReferencedAssemblies()
											.Select(r =>
											{
												return new DllReference()
												{
													Id = r.Name,
													IsPrivate = true,
													Version = new SemVersion(r.Version)
												};
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

									addin.IconUrl = string.IsNullOrEmpty(iconUrl) ? null : new Uri(iconUrl);
									addin.NuGetPackageVersion = packageVersion;
									addin.Frameworks = frameworks;
									addin.References = dllReferences;
									addin.IsPrerelease |= isPreRelease;
									if (addin.GithubRepoUrl == null) addin.GithubRepoUrl = string.IsNullOrEmpty(projectUrl) ? null : new Uri(projectUrl);
									if (addin.Name.EndsWith(".Module", StringComparison.OrdinalIgnoreCase)) addin.Type = AddinType.Module;
									if (addin.Type == AddinType.Unknown && !string.IsNullOrEmpty(assemblyPath)) addin.Type = AddinType.Addin;
									if (!string.IsNullOrEmpty(assemblyPath)) addin.DllName = Path.GetFileName(assemblyPath);

									if (addin.Type == AddinType.Unknown)
									{
										throw new Exception("We are unable to determine the type of this addin. One likely reason is that it contains multiple DLLs but none of them respect the naming convention.");
									}
								}
							}
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
			try
			{
				var cleanPath = assemblyPath.Replace('/', '\\');
				if (cleanPath.IndexOf('%') > -1)
				{
					cleanPath = Uri.UnescapeDataString(cleanPath);
				}

				using (var assemblyStream = package.GetStream(cleanPath))
				{
					using (MemoryStream decompressedStream = new MemoryStream())
					{
						assemblyStream.CopyTo(decompressedStream);
						decompressedStream.Position = 0;
						return loadContext.LoadFromStream(decompressedStream);
					}
				}
			}
			catch (FileLoadException e)
			{
				// Note: intentionally discarding the original exception because I want to ensure the following message is displayed in the 'Exceptions' report
				throw new FileLoadException($"An error occured while loading {Path.GetFileName(assemblyPath)} from the Nuget package. {e.Message}");
			}
		}
	}
}
