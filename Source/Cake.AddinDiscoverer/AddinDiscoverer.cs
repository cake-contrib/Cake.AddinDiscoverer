using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator;
using CsvHelper;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using Octokit;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer
{
	internal class AddinDiscoverer
	{
		private const string FILE_MODE = "100644";
		private const string PRODUCT_NAME = "Cake.AddinDiscoverer";
		private const string ISSUE_TITLE = "Recommended changes resulting from automated audit";
		private const string CAKE_CONTRIB_ICON_URL = "https://cdn.rawgit.com/cake-contrib/graphics/a5cf0f881c390650144b2243ae551d5b9f836196/png/cake-contrib-medium.png";
		private const string CAKE_RECIPE_ISSUE_TITLE = "Addin references need to be updated";
		private const string CAKECONTRIB_ICON_URL = "https://cdn.rawgit.com/cake-contrib/graphics/a5cf0f881c390650144b2243ae551d5b9f836196/png/cake-contrib-medium.png";
		private const string UNKNOWN_VERSION = "Unknown";
		private const int MAX_GITHUB_CONCURENCY = 10;
		private const int MAX_NUGET_CONCURENCY = 25; // 25 seems like a safe value but I suspect that nuget allows a much large number on concurrent connections.
		private const string GREEN_EMOJI = ":white_check_mark: ";
		private const string RED_EMOJI = ":small_red_triangle: ";
		private const string CSV_DATE_FORMAT = "yyyy-MM-dd HH:mm:ss";

		private const string CAKE_REPO_OWNER = "cake-build";
		private const string CAKE_WEBSITE_REPO_NAME = "website";
		private const string CAKE_CONTRIB_REPO_OWNER = "cake-contrib";
		private const string CAKE_CONTRIB_REPO_NAME = "Home";
		private const string CAKE_RECIPE_REPO_NAME = "Cake.Recipe";

		private static readonly SemVersion _unknownVersion = new SemVersion(0, 0, 0);

		private readonly Options _options;
		private readonly string _tempFolder;
		private readonly string _packagesFolder;
		private readonly string _excelReportPath;
		private readonly string _markdownReportPath;
		private readonly IGitHubClient _githubClient;
		private readonly string _statsSaveLocation;
		private readonly string _graphSaveLocation;
		private readonly string _addinDiscovererVersion;

		private readonly CakeVersion[] _cakeVersions = new[]
		{
			new CakeVersion { Version = new SemVersion(0, 26, 0), Framework = "netstandard2.0" },
			new CakeVersion { Version = new SemVersion(0, 28, 0), Framework = "netstandard2.0" }
		};

		// This is a hardcoded list of addins that we specifically want to exclude from our reports
		private readonly string[] _blackListedAddins = new string[]
		{
			"Cake.Bakery",
			"Cake.Common",
			"Cake.Core",
			"Cake.CoreCLR",
			"Cake.Email.Common"
		};

#pragma warning disable SA1000 // Keywords should be spaced correctly
#pragma warning disable SA1008 // Opening parenthesis should be spaced correctly
#pragma warning disable SA1009 // Closing parenthesis should be spaced correctly
		private readonly (string Header, ExcelHorizontalAlignment Align, Func<AddinMetadata, string> GetContent, Func<AddinMetadata, CakeVersion, Color> GetCellColor, Func<AddinMetadata, Uri> GetHyperLink, AddinType ApplicableTo, DataDestination Destination)[] _reportColumns = new(string Header, ExcelHorizontalAlignment Align, Func<AddinMetadata, string> GetContent, Func<AddinMetadata, CakeVersion, Color> GetCellColor, Func<AddinMetadata, Uri> GetHyperLink, AddinType ApplicableTo, DataDestination Destination)[]
		{
			(
				"Name",
				ExcelHorizontalAlignment.Left,
				(addin) => addin.Name,
				(addin, cakeVersion) => Color.Empty,
				(addin) => addin.GithubRepoUrl ?? addin.NugetPackageUrl,
				AddinType.All,
				DataDestination.All
			),
			(
				"Maintainer",
				ExcelHorizontalAlignment.Left,
				(addin) => addin.GetMaintainerName(),
				(addin, cakeVersion) => Color.Empty,
				(addin) => null,
				AddinType.All,
				DataDestination.Excel | DataDestination.MarkdownForRecipes
			),
			(
				"Cake Core Version",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.CakeCoreVersion == null ? string.Empty : addin.AnalysisResult.CakeCoreVersion == _unknownVersion ? UNKNOWN_VERSION : addin.AnalysisResult.CakeCoreVersion.ToString(3),
				(addin, cakeVersion) => addin.AnalysisResult.CakeCoreVersion == null ? Color.Empty : (IsCakeVersionUpToDate(addin.AnalysisResult.CakeCoreVersion, cakeVersion.Version) ? Color.LightGreen : Color.Red),
				(addin) => null,
				AddinType.Addin | AddinType.Module,
				DataDestination.All
			),
			(
				"Cake Core IsPrivate",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.CakeCoreVersion == null ? string.Empty : addin.AnalysisResult.CakeCoreIsPrivate.ToString().ToLower(),
				(addin, cakeVersion) => addin.AnalysisResult.CakeCoreVersion == null ? Color.Empty : (addin.AnalysisResult.CakeCoreIsPrivate ? Color.LightGreen : Color.Red),
				(addin) => null,
				AddinType.Addin | AddinType.Module,
				DataDestination.All
			),
			(
				"Cake Common Version",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.CakeCommonVersion == null ? string.Empty : addin.AnalysisResult.CakeCommonVersion == _unknownVersion ? UNKNOWN_VERSION : addin.AnalysisResult.CakeCommonVersion.ToString(3),
				(addin, cakeVersion) => addin.AnalysisResult.CakeCommonVersion == null ? Color.Empty : (IsCakeVersionUpToDate(addin.AnalysisResult.CakeCommonVersion, cakeVersion.Version) ? Color.LightGreen : Color.Red),
				(addin) => null,
				AddinType.Addin | AddinType.Module,
				DataDestination.All
			),
			(
				"Cake Common IsPrivate",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.CakeCommonVersion == null ? string.Empty : addin.AnalysisResult.CakeCommonIsPrivate.ToString().ToLower(),
				(addin, cakeVersion) => addin.AnalysisResult.CakeCommonVersion == null ? Color.Empty : (addin.AnalysisResult.CakeCommonIsPrivate ? Color.LightGreen : Color.Red),
				(addin) => null,
				AddinType.Addin | AddinType.Module,
				DataDestination.All
			),
			(
				"Framework",
				ExcelHorizontalAlignment.Center,
				(addin) => string.Join(", ", addin.Frameworks),
				(addin, cakeVersion) => (addin.Frameworks ?? Array.Empty<string>()).Length == 0 ? Color.Empty : (IsFrameworkUpToDate(addin.Frameworks, cakeVersion.Framework) ? Color.LightGreen : Color.Red),
				(addin) => null,
				AddinType.Addin | AddinType.Module,
				DataDestination.All
			),
			(
				"Icon",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.UsingCakeContribIcon.ToString().ToLower(),
				(addin, cakeVersion) => addin.AnalysisResult.UsingCakeContribIcon ? Color.LightGreen : Color.Red,
				(addin) => null,
				AddinType.All,
				DataDestination.Excel | DataDestination.MarkdownForRecipes
			),
			(
				"Transferred to cake-contrib",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.TransferedToCakeContribOrganisation.ToString().ToLower(),
				(addin, cakeVersion) => addin.AnalysisResult.TransferedToCakeContribOrganisation ? Color.LightGreen : Color.Red,
				(addin) => null,
				AddinType.All,
				DataDestination.Excel | DataDestination.MarkdownForRecipes
			),
		};
#pragma warning restore SA1009 // Closing parenthesis should be spaced correctly
#pragma warning restore SA1008 // Opening parenthesis should be spaced correctly
#pragma warning restore SA1000 // Keywords should be spaced correctly

		public AddinDiscoverer(Options options)
		{
			_options = options;
			_tempFolder = Path.Combine(_options.TemporaryFolder, PRODUCT_NAME);
			_packagesFolder = Path.Combine(_tempFolder, "packages");
			_excelReportPath = Path.Combine(_tempFolder, "Audit.xlsx");
			_markdownReportPath = Path.Combine(_tempFolder, "Audit.md");
			_statsSaveLocation = Path.Combine(_tempFolder, "Audit_stats.csv");
			_graphSaveLocation = Path.Combine(_tempFolder, "Audit_progress.png");

			// Setup the Github client
			var credentials = new Credentials(_options.GithubUsername, _options.GithuPassword);
			var connection = new Connection(new ProductHeaderValue(PRODUCT_NAME))
			{
				Credentials = credentials,
			};
			_githubClient = new GitHubClient(connection);

			var assemblyVersion = typeof(AddinDiscoverer).GetTypeInfo().Assembly.GetName().Version;
#if DEBUG
			_addinDiscovererVersion = "DEBUG";
#else
			_addinDiscovererVersion = $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
#endif
		}

		public async Task LaunchDiscoveryAsync()
		{
			try
			{
				// Clean up
				await Cleanup().ConfigureAwait(false);

				Console.WriteLine("Auditing the Cake Addins");

				// Discover Cake Addins by querying Nuget.org (also download the most recent package)
				var addins = await DiscoverCakeAddinsAsync().ConfigureAwait(false);
				EnsureAtLeastOneAddin(addins);

				// Clean black listed addins
				addins = addins
					.Where(addin => !_blackListedAddins.Any(blackListedAddinName => blackListedAddinName == addin.Name))
					.ToArray();
				EnsureAtLeastOneAddin(addins);

				// Validate the project URL
				addins = await ValidateProjectUrlAsync(addins).ConfigureAwait(false);

				// Determine if an issue already exists in the Github repo
				if (_options.CreateGithubIssue)
				{
					addins = await FindGithubIssueAsync(addins).ConfigureAwait(false);
				}

				// Analyze the nuget metadata
				addins = AnalyzeNugetMetadata(addins);

				// Analyze
				addins = AnalyzeAddin(addins);

				// Create an issue in the Github repo
				if (_options.CreateGithubIssue)
				{
					addins = await CreateGithubIssueAsync(addins).ConfigureAwait(false);
				}

				// Generate the excel report and save to a file
				GenerateExcelReport(addins);

				// Generate the markdown report and write to file
				await GenerateMarkdownReportAsync(addins).ConfigureAwait(false);

				// Update the CSV file containing historical statistics (used to generate graph)
				await UpdateStatsAsync(addins).ConfigureAwait(false);

				// Generate the graph showing how many addins are compatible with Cake over time
				GenerateStatsGraph();

				// Commit the changed files (such as reports, stats CSV, graph, etc.) to the cake-contrib repo
				await CommitChangesToRepoAsync().ConfigureAwait(false);

				// Synchronize the YAML files on the Cake web site with packages discovered on Nuget.org
				if (_options.SynchronizeYaml)
				{
					await SynchronizeYmlFilesAsync(addins).ConfigureAwait(false);
				}

				// Update CakeRecipe
				if (_options.UpdateCakeRecipeReferences)
				{
					var cakeRecipeIssueId = await FindCakeRecipeGithubIssueAsync().ConfigureAwait(false);
					if (!cakeRecipeIssueId.HasValue)
					{
						await UpdateCakeRecipeFilesAsync(addins).ConfigureAwait(false);
					}
				}
			}
			catch (Exception e)
			{
				Console.WriteLine($"{Environment.NewLine}***** AN EXCEPTION HAS OCCURED *****");
				Console.WriteLine(e.Demystify().ToString());
			}
		}

		private static bool IsCakeVersionUpToDate(SemVersion currentVersion, SemVersion desiredVersion)
		{
			if (desiredVersion == null) throw new ArgumentNullException(nameof(desiredVersion));

			return currentVersion == null || currentVersion >= desiredVersion;
		}

		private static bool IsFrameworkUpToDate(string[] currentFrameworks, string desiredFramework)
		{
			if (currentFrameworks == null) return false;
			else if (currentFrameworks.Length != 1) return false;
			else return currentFrameworks[0].EqualsIgnoreCase(desiredFramework);
		}

		private static Assembly LoadAssemblyFromPackage(IPackageCoreReader package, string assemblyPath)
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
						return AssemblyLoadContext.Default.LoadFromStream(decompressedStream);
					}
				}
			}
			catch (FileLoadException e)
			{
				// Note: intentionally discarding the original exception because I want to ensure the following message is displayed in the 'Exceptions' report
				throw new FileLoadException($"An error occured while loading {Path.GetFileName(assemblyPath)} from the Nuget package. {e.Message}");
			}
		}

		private static string GenerateYamlFile(AddinMetadata addin)
		{
			var yamlContent = new StringBuilder();

			yamlContent.AppendLine($"Name: {addin.Name}");
			yamlContent.AppendLine($"Nuget: {addin.Name}");
			yamlContent.AppendLine("Assemblies:");
			yamlContent.AppendLine($"- \"/**/{addin.Name}.dll\"");
			yamlContent.AppendLine($"Repository: {addin.GithubRepoUrl ?? addin.NugetPackageUrl}");
			yamlContent.AppendLine($"Author: {addin.GetMaintainerName()}");
			yamlContent.AppendLine($"Description: \"{addin.Description}\"");
			if (addin.IsPrerelease) yamlContent.AppendLine("Prerelease: \"true\"");
			yamlContent.AppendLine("Categories:");
			yamlContent.AppendLine(string.Join(Environment.NewLine, addin.Tags.Select(tag => $"- {tag}")));

			return yamlContent.ToString();
		}

		private static DataDestination GetMarkdownDestinationForType(AddinType type)
		{
			if (type == AddinType.Addin) return DataDestination.MarkdownForAddins;
			else if (type == AddinType.Recipes) return DataDestination.MarkdownForRecipes;
			else throw new ArgumentException($"Unable to determine the DataDestination for type {type}");
		}

		private async Task Cleanup()
		{
			Console.WriteLine("Clean up");

			if (_options.ClearCache && Directory.Exists(_tempFolder))
			{
				Directory.Delete(_tempFolder, true);
				await Task.Delay(500).ConfigureAwait(false);
			}

			if (!Directory.Exists(_tempFolder))
			{
				Directory.CreateDirectory(_tempFolder);
				await Task.Delay(500).ConfigureAwait(false);
			}

			if (!Directory.Exists(_packagesFolder))
			{
				Directory.CreateDirectory(_packagesFolder);
				await Task.Delay(500).ConfigureAwait(false);
			}

			if (File.Exists(_excelReportPath)) File.Delete(_excelReportPath);
			if (File.Exists(_markdownReportPath)) File.Delete(_markdownReportPath);
			if (File.Exists(_statsSaveLocation)) File.Delete(_statsSaveLocation);
			if (File.Exists(_graphSaveLocation)) File.Delete(_graphSaveLocation);

			foreach (var markdownReport in Directory.EnumerateFiles(_tempFolder, $"{Path.GetFileNameWithoutExtension(_markdownReportPath)}*.md"))
			{
				File.Delete(markdownReport);
			}
		}

		private async Task<AddinMetadata[]> DiscoverCakeAddinsAsync()
		{
			if (string.IsNullOrEmpty(_options.AddinName)) Console.WriteLine("  Discovering Cake addins by querying Nuget.org");
			else Console.WriteLine($"  Discovering {_options.AddinName} by querying Nuget.org");

			var providers = new List<Lazy<INuGetResourceProvider>>();
			providers.AddRange(NuGet.Protocol.Core.Types.Repository.Provider.GetCoreV3());  // Add v3 API support
			var packageSource = new PackageSource("https://api.nuget.org/v3/index.json");
			var sourceRepository = new SourceRepository(packageSource, providers);
			var nugetPackageDownloadClient = sourceRepository.GetResource<DownloadResource>();

			var take = 50;
			var skip = 0;
			var searchTerm = "Cake";
			var filters = new SearchFilter(true)
			{
				IncludeDelisted = false,
				OrderBy = SearchOrderBy.Id
			};

			var addinPackages = new List<IPackageSearchMetadata>(take);

			//--------------------------------------------------
			// STEP 1 - Get the metadata from Nuget.org
			if (!string.IsNullOrEmpty(_options.AddinName))
			{
				// Get metadata for one specific package
				var nugetPackageMetadataClient = sourceRepository.GetResource<PackageMetadataResource>();
				var searchMetadata = await nugetPackageMetadataClient.GetMetadataAsync(_options.AddinName, true, false, NullLogger.Instance, CancellationToken.None).ConfigureAwait(false);
				var mostRecentPackageMetadata = searchMetadata.OrderByDescending(p => p.Published).FirstOrDefault();
				if (mostRecentPackageMetadata != null)
				{
					addinPackages.Add(mostRecentPackageMetadata);
				}
			}
			else
			{
				var nugetSearchClient = sourceRepository.GetResource<PackageSearchResource>();

				// Search for all package matching the search term
				while (true)
				{
					var searchResult = await nugetSearchClient.SearchAsync(searchTerm, filters, skip, take, NullLogger.Instance, CancellationToken.None).ConfigureAwait(false);
					skip += take;

					if (!searchResult.Any())
					{
						break;
					}

					addinPackages.AddRange(searchResult.Where(r => r.Identity.Id.StartsWith("Cake.")));
				}
			}

			//--------------------------------------------------
			// STEP 2 - Download packages
			await addinPackages
				.ForEachAsync(
					async package =>
					{
						var packageFileName = Path.Combine(_packagesFolder, $"{package.Identity.Id}.{package.Identity.Version.ToNormalizedString()}.nupkg");
						if (!File.Exists(packageFileName))
						{
							// Delete prior versions of this package
							foreach (string f in Directory.EnumerateFiles(_packagesFolder, $"{package.Identity.Id}.*.nupkg"))
							{
								File.Delete(f);
							}

							// Download the latest version of the package
							using (var sourceCacheContext = new SourceCacheContext() { NoCache = true })
							{
								var context = new PackageDownloadContext(sourceCacheContext, Path.GetTempPath(), true);

								using (var result = await nugetPackageDownloadClient.GetDownloadResourceResultAsync(package.Identity, context, string.Empty, NullLogger.Instance, CancellationToken.None))
								{
									if (result.Status == DownloadResourceResultStatus.Cancelled)
									{
										throw new OperationCanceledException();
									}
									else if (result.Status == DownloadResourceResultStatus.NotFound)
									{
										throw new Exception(string.Format("Package '{0} {1}' not found", package.Identity.Id, package.Identity.Version));
									}
									else
									{
										using (var fileStream = File.OpenWrite(packageFileName))
										{
											await result.PackageStream.CopyToAsync(fileStream);
										}
									}
								}
							}
						}
					}, MAX_NUGET_CONCURENCY)
				.ConfigureAwait(false);

			//--------------------------------------------------
			// STEP 3 - Convert metadata from nuget into our own metadata
			var addinsMetadata = addinPackages
				.Select(package =>
				{
					var addinMetadata = new AddinMetadata()
					{
						AnalysisResult = new AddinAnalysisResult(),
						Maintainer = package.Authors,
						Description = package.Description,
						GithubRepoUrl = package.ProjectUrl != null && package.ProjectUrl.Host.Contains("github.com") ? package.ProjectUrl : null,
						IconUrl = package.IconUrl,
						Name = package.Identity.Id,
						NugetPackageUrl = new Uri($"https://www.nuget.org/packages/{package.Identity.Id}/"),
						NugetPackageVersion = package.Identity.Version.ToNormalizedString(),
						IsDeprecated = false,
						IsPrerelease = package.Identity.Version.IsPrerelease,
						Tags = package.Tags
							.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
							.Select(tag => tag.Trim())
							.ToArray(),
						Type = AddinType.Unknown
					};

					if (package.Title.Contains("[DEPRECATED]", StringComparison.OrdinalIgnoreCase))
					{
						addinMetadata.IsDeprecated = true;
						addinMetadata.AnalysisResult.Notes = package.Description;
					}

					return addinMetadata;
				})
				.ToArray();

			return addinsMetadata;
		}

		private async Task<AddinMetadata[]> ValidateProjectUrlAsync(IEnumerable<AddinMetadata> addins)
		{
			Console.WriteLine("  Validate Github repo URLs");

			var cakeContribRepositories = await _githubClient.Repository.GetAllForUser(CAKE_CONTRIB_REPO_OWNER).ConfigureAwait(false);

			var results = addins
				.Select(addin =>
				{
					if (addin.GithubRepoUrl == null ||
						addin.GithubRepoUrl.Host != "github.com" ||
						addin.GithubRepoOwner != CAKE_CONTRIB_REPO_OWNER)
					{
						try
						{
							var repo = cakeContribRepositories.FirstOrDefault(r => r.Name == addin.Name);
							if (repo != null)
							{
								addin.GithubRepoUrl = new Uri(repo.HtmlUrl);
							}
						}
						catch (Exception e)
						{
							addin.AnalysisResult.Notes += $"ValidateProjectUrlAsync: {e.GetBaseException().Message}{Environment.NewLine}";
						}
					}
					return addin;
				});

			return results.ToArray();
		}

		private AddinMetadata[] AnalyzeNugetMetadata(IEnumerable<AddinMetadata> addins)
		{
			Console.WriteLine("  Analyzing nuget packages");

			var results = addins
				.Select(addin =>
				{
					try
					{
						var packageFileName = Path.Combine(_packagesFolder, $"{addin.Name}.{addin.NugetPackageVersion}.nupkg");
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

									var packageDependencies = package.GetPackageDependencies()
										.SelectMany(d => d.Packages)
										.Select(p =>
										{
											var normalizedVersion = (p.VersionRange.HasUpperBound ? p.VersionRange.MaxVersion : p.VersionRange.MinVersion).Version;
											return new DllReference()
											{
												Id = p.Id,
												IsPrivate = false,
												Version = new SemVersion(normalizedVersion)
											};
										})
										.ToArray();

									var assembliesPath = package.GetFiles()
										.Where(f =>
										{
											return Path.GetExtension(f).EqualsIgnoreCase(".dll") &&
												!Path.GetFileNameWithoutExtension(f).EqualsIgnoreCase("Cake.Core") &&
												!Path.GetFileNameWithoutExtension(f).EqualsIgnoreCase("Cake.Common") &&
												(
													string.IsNullOrEmpty(Path.GetDirectoryName(f)) ||
													f.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) ||
													f.StartsWith("lib/", StringComparison.OrdinalIgnoreCase)
												);
										})
										.ToArray();

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

										// There are multiple DLLs in this package and none of them match the naming convention
										else
										{
											throw new Exception($"The NuGet package does not contain a DLL named {addin.Name}.dll");
										}
									}

									// Find the DLL references
									var dllReferences = Array.Empty<DllReference>();
									if (!string.IsNullOrEmpty(assemblyPath))
									{
										var assembly = LoadAssemblyFromPackage(package, assemblyPath);
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
									addin.NugetPackageVersion = packageVersion;
									addin.Frameworks = frameworks;
									addin.References = dllReferences;
									if (addin.GithubRepoUrl == null) addin.GithubRepoUrl = string.IsNullOrEmpty(projectUrl) ? null : new Uri(projectUrl);
									if (addin.Name.EndsWith(".Module", StringComparison.OrdinalIgnoreCase)) addin.Type = AddinType.Module;
									if (addin.Type == AddinType.Unknown && !string.IsNullOrEmpty(assemblyPath)) addin.Type = AddinType.Addin;

									if (addin.Type == AddinType.Unknown)
									{
										throw new Exception("The Nuget package for this addin contains neither '.dll' nor '.cake' files. Therefore we are unable to determine the type of this addin.");
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
				});

			return results.ToArray();
		}

		private async Task<AddinMetadata[]> FindGithubIssueAsync(IEnumerable<AddinMetadata> addins)
		{
			Console.WriteLine("  Finding Github issues");

			var addinsMetadata = await addins
				.ForEachAsync(
					async addin =>
					{
						if (addin.GithubIssueUrl == null && addin.GithubRepoUrl != null)
						{
							var request = new RepositoryIssueRequest()
							{
								Creator = _options.GithubUsername,
								State = ItemStateFilter.Open,
								SortProperty = IssueSort.Created,
								SortDirection = SortDirection.Descending
							};

							try
							{
								var issues = await _githubClient.Issue.GetAllForRepository(addin.GithubRepoOwner, addin.GithubRepoName, request).ConfigureAwait(false);
								var issue = issues.FirstOrDefault(i => i.Title.EqualsIgnoreCase(ISSUE_TITLE) || i.Body.StartsWith("We performed an automated audit of your Cake addin", StringComparison.OrdinalIgnoreCase));

								if (issue != null)
								{
									addin.GithubIssueUrl = new Uri(issue.Url);
									addin.GithubIssueId = issue.Number;
								}
							}
							catch (Exception e)
							{
								addin.AnalysisResult.Notes += $"FindGithubIssueAsync: {e.GetBaseException().Message}{Environment.NewLine}";
							}
						}

						return addin;
					}, MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);

			return addinsMetadata;
		}

		private async Task SynchronizeYmlFilesAsync(IEnumerable<AddinMetadata> addins)
		{
			Console.WriteLine("  Synchronizing yml files on the Cake web site");

			const string ISSUE_TITLE = "Synchronizing YAML files";

			// --------------------------------------------------
			// Check if there is already an open issue
			var request = new RepositoryIssueRequest()
			{
				Creator = _options.GithubUsername,
				State = ItemStateFilter.Open,
				SortProperty = IssueSort.Created,
				SortDirection = SortDirection.Descending
			};

			var issues = await _githubClient.Issue.GetAllForRepository(CAKE_REPO_OWNER, CAKE_WEBSITE_REPO_NAME, request).ConfigureAwait(false);
			if (issues.Any(i => i.Title == ISSUE_TITLE)) return;

			// --------------------------------------------------
			// Discover if any files need to be added/deleted/modified
			var directoryContent = await _githubClient.Repository.Content.GetAllContents(CAKE_REPO_OWNER, CAKE_WEBSITE_REPO_NAME, "addins").ConfigureAwait(false);
			var yamlFiles = directoryContent
				.Where(file => file.Name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
				.Where(file => !string.IsNullOrEmpty(_options.AddinName) ? Path.GetFileNameWithoutExtension(file.Name) == _options.AddinName : true)
				.ToArray();

			var yamlToBeDeleted = yamlFiles
				.Where(f =>
				{
					var addin = addins.FirstOrDefault(a => a.Name == Path.GetFileNameWithoutExtension(f.Name));
					return addin == null || addin.IsDeprecated;
				})
				.Where(f => f.Name != "Magic-Chunks.yml") // Ensure that MagicChunk's yaml file is not deleted despite the fact that is doesn't follow the naming convention. See: https://github.com/cake-build/website/issues/535#issuecomment-399692891
				.OrderBy(f => f.Name)
				.ToArray();

			var addinsWithContent = await addins
				.Where(a => yamlFiles.Any(f => Path.GetFileNameWithoutExtension(f.Name) == a.Name))
				.ForEachAsync(
					async addin =>
					{
						var contents = await _githubClient.Repository.Content.GetAllContents(CAKE_REPO_OWNER, CAKE_WEBSITE_REPO_NAME, $"addins/{addin.Name}.yml").ConfigureAwait(false);
						return new
						{
							Addin = addin,
							CurrentContent = contents[0].Content
								.Replace("\r\n", "\n")
								.Replace("\r", "\n")
								.Replace("\n", Environment.NewLine),
							NewContent = GenerateYamlFile(addin)
						};
					}, MAX_NUGET_CONCURENCY)
				.ConfigureAwait(false);

			var addinsToBeUpdated = addinsWithContent
				.Where(addin => addin.CurrentContent != addin.NewContent)
				.Where(addin => !addin.Addin.IsDeprecated)
				.OrderBy(addin => addin.Addin.Name)
				.ToArray();

			var addinsWithoutYaml = addins
				.Where(addin => !addin.IsDeprecated)
				.Where(addin => !yamlFiles.Any(f => Path.GetFileNameWithoutExtension(f.Name) == addin.Name))
				.OrderBy(addin => addin.Name)
				.ToArray();

			if (!yamlToBeDeleted.Any() && !addinsWithoutYaml.Any() && !addinsToBeUpdated.Any()) return;

			// --------------------------------------------------
			// Create issue
			var newIssue = new NewIssue(ISSUE_TITLE)
			{
				Body = $"The Cake.AddinDiscoverer tool has discovered discrepencies between the YAML files currently on Cake's web site and the packages discovered on Nuget.org:{Environment.NewLine}" +
					$"{Environment.NewLine}YAML files to be deleted:{Environment.NewLine}{string.Join(Environment.NewLine, yamlToBeDeleted.Select(f => $"- {f.Name}"))}{Environment.NewLine}" +
					$"{Environment.NewLine}YAML files to be created:{Environment.NewLine}{string.Join(Environment.NewLine, addinsWithoutYaml.Select(a => $"- {a.Name}"))}{Environment.NewLine}" +
					$"{Environment.NewLine}YAML files to be updated:{Environment.NewLine}{string.Join(Environment.NewLine, addinsToBeUpdated.Select(a => $"- {a.Addin.Name}"))}{Environment.NewLine}"
			};
			var issue = await _githubClient.Issue.Create(CAKE_REPO_OWNER, CAKE_WEBSITE_REPO_NAME, newIssue).ConfigureAwait(false);

			// --------------------------------------------------
			// Commit changes to a new branch
			var developBranchName = "develop";
			var newBranchName = $"synchronize_yaml_files_{DateTime.UtcNow:yyyy_MM_dd_HH_mm_ss}";

			var developReference = await _githubClient.Git.Reference.Get(CAKE_REPO_OWNER, CAKE_WEBSITE_REPO_NAME, $"heads/{developBranchName}").ConfigureAwait(false);
			var newReference = new NewReference($"heads/{newBranchName}", developReference.Object.Sha);
			var newBranch = await _githubClient.Git.Reference.Create(CAKE_REPO_OWNER, CAKE_WEBSITE_REPO_NAME, newReference).ConfigureAwait(false);

			var latestCommit = await _githubClient.Git.Commit.Get(CAKE_REPO_OWNER, CAKE_WEBSITE_REPO_NAME, newBranch.Object.Sha);

			if (yamlToBeDeleted.Any())
			{
				var nt = new NewTree();
				var currentTree = await _githubClient.Git.Tree.GetRecursive(CAKE_REPO_OWNER, CAKE_WEBSITE_REPO_NAME, latestCommit.Tree.Sha).ConfigureAwait(false);
				currentTree.Tree
					.Where(x => x.Type != TreeType.Tree)
					.Select(x => new NewTreeItem
					{
						Path = x.Path,
						Mode = x.Mode,
						Type = x.Type.Value,
						Sha = x.Sha
					})
					.ToList()
					.ForEach(x => nt.Tree.Add(x));

				foreach (var yamlFile in yamlToBeDeleted)
				{
					nt.Tree.Remove(nt.Tree.Where(x => x.Path.Equals(yamlFile.Path)).First());
				}

				// Commit changes
				var newTree = await _githubClient.Git.Tree.Create(CAKE_REPO_OWNER, CAKE_WEBSITE_REPO_NAME, nt);
				var newCommit = new NewCommit($"Delete YAML files that do not have a corresponding Nuget package", newTree.Sha, newBranch.Object.Sha);
				latestCommit = await _githubClient.Git.Commit.Create(CAKE_REPO_OWNER, CAKE_WEBSITE_REPO_NAME, newCommit);
			}

			if (addinsWithoutYaml.Any())
			{
				var nt = new NewTree();

				foreach (var addin in addinsWithoutYaml)
				{
					var yamlFileBlob = new NewBlob
					{
						Encoding = EncodingType.Utf8,
						Content = GenerateYamlFile(addin)
					};
					var yamlFileBlobRef = await _githubClient.Git.Blob.Create(CAKE_REPO_OWNER, CAKE_WEBSITE_REPO_NAME, yamlFileBlob).ConfigureAwait(false);
					nt.Tree.Add(new NewTreeItem
					{
						Path = $"addins/{addin.Name}.yml",
						Mode = FILE_MODE,
						Type = TreeType.Blob,
						Sha = yamlFileBlobRef.Sha
					});
				}

				var newTree = await _githubClient.Git.Tree.Create(CAKE_REPO_OWNER, CAKE_WEBSITE_REPO_NAME, nt);
				var newCommit = new NewCommit($"Add YAML files for Nuget packages we discovered", newTree.Sha, newBranch.Object.Sha);
				latestCommit = await _githubClient.Git.Commit.Create(CAKE_REPO_OWNER, CAKE_WEBSITE_REPO_NAME, newCommit);
			}

			if (addinsToBeUpdated.Any())
			{
				var nt = new NewTree();

				foreach (var addin in addinsToBeUpdated)
				{
					var yamlFileBlob = new NewBlob
					{
						Encoding = EncodingType.Utf8,
						Content = addin.NewContent
					};
					var yamlFileBlobRef = await _githubClient.Git.Blob.Create(CAKE_REPO_OWNER, CAKE_WEBSITE_REPO_NAME, yamlFileBlob).ConfigureAwait(false);
					nt.Tree.Add(new NewTreeItem
					{
						Path = $"addins/{addin.Addin.Name}.yml",
						Mode = FILE_MODE,
						Type = TreeType.Blob,
						Sha = yamlFileBlobRef.Sha
					});
				}

				var newTree = await _githubClient.Git.Tree.Create(CAKE_REPO_OWNER, CAKE_WEBSITE_REPO_NAME, nt);
				var newCommit = new NewCommit($"Update YAML files to match metadata from Nuget", newTree.Sha, newBranch.Object.Sha);
				latestCommit = await _githubClient.Git.Commit.Create(CAKE_REPO_OWNER, CAKE_WEBSITE_REPO_NAME, newCommit);
			}

			await _githubClient.Git.Reference.Update(CAKE_REPO_OWNER, CAKE_WEBSITE_REPO_NAME, $"heads/{newBranchName}", new ReferenceUpdate(latestCommit.Sha));

			// --------------------------------------------------
			// Submit pull request
			var newPullRequest = new NewPullRequest("Update YAML files", newBranchName, developBranchName)
			{
				Body = $"Resolves #{issue.Number}"
			};
			await _githubClient.PullRequest.Create(CAKE_REPO_OWNER, CAKE_WEBSITE_REPO_NAME, newPullRequest).ConfigureAwait(false);
		}

		private AddinMetadata[] AnalyzeAddin(IEnumerable<AddinMetadata> addins)
		{
			Console.WriteLine("  Analyzing addins");

			var results = addins
				.Select(addin =>
				{
					if (addin.References != null)
					{
						var cakeCommonReference = addin.References.Where(r => r.Id.EqualsIgnoreCase("Cake.Common"));
						if (cakeCommonReference.Any())
						{
							var cakeCommonVersion = cakeCommonReference.Min(r => r.Version);
							var cakeCommonIsPrivate = cakeCommonReference.All(r => r.IsPrivate);
							addin.AnalysisResult.CakeCommonVersion = cakeCommonVersion ?? _unknownVersion;
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
							addin.AnalysisResult.CakeCoreVersion = cakeCoreVersion ?? _unknownVersion;
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
						addin.AnalysisResult.Notes += $"This addin seem to be referencing neither Cake.Core nor Cake.Common.{Environment.NewLine}";
					}

					addin.AnalysisResult.UsingCakeContribIcon = addin.IconUrl != null && addin.IconUrl.AbsoluteUri.EqualsIgnoreCase(CAKE_CONTRIB_ICON_URL);
					addin.AnalysisResult.TransferedToCakeContribOrganisation = addin.GithubRepoOwner?.Equals(CAKE_CONTRIB_REPO_OWNER, StringComparison.OrdinalIgnoreCase) ?? false;

					return addin;
				});

			return results.ToArray();
		}

		private async Task<AddinMetadata[]> CreateGithubIssueAsync(IEnumerable<AddinMetadata> addins)
		{
			Console.WriteLine("  Creating Github issues");

			var recommendedCakeVersion = _cakeVersions
				.OrderByDescending(cakeVersion => cakeVersion.Version)
				.First();

			var addinsMetadata = await addins
				.ForEachAsync(
					async addin =>
					{
						if (addin.Type != AddinType.Recipes && addin.GithubRepoUrl != null && !addin.GithubIssueId.HasValue)
						{
							var issuesDescription = new StringBuilder();
							if (addin.AnalysisResult.CakeCoreVersion == _unknownVersion)
							{
								issuesDescription.AppendLine($"- [ ] We were unable to determine what version of Cake.Core your addin is referencing. Please make sure you are referencing {recommendedCakeVersion.Version}");
							}
							else if (!IsCakeVersionUpToDate(addin.AnalysisResult.CakeCoreVersion, recommendedCakeVersion.Version))
							{
								issuesDescription.AppendLine($"- [ ] You are currently referencing Cake.Core {addin.AnalysisResult.CakeCoreVersion.ToString()}. Please upgrade to {recommendedCakeVersion.Version.ToString()}");
							}

							if (addin.AnalysisResult.CakeCommonVersion == _unknownVersion)
							{
								issuesDescription.AppendLine($"- [ ] We were unable to determine what version of Cake.Common your addin is referencing. Please make sure you are referencing {recommendedCakeVersion.Version}");
							}
							else if (!IsCakeVersionUpToDate(addin.AnalysisResult.CakeCommonVersion, recommendedCakeVersion.Version))
							{
								issuesDescription.AppendLine($"- [ ] You are currently referencing Cake.Common {addin.AnalysisResult.CakeCommonVersion.ToString()}. Please upgrade to {recommendedCakeVersion.Version.ToString()}");
							}

							if (!addin.AnalysisResult.CakeCoreIsPrivate) issuesDescription.AppendLine($"- [ ] The Cake.Core reference should be private. Specifically, your addin's `.csproj` should have a line similar to this: `<PackageReference Include=\"Cake.Core\" Version=\"{recommendedCakeVersion.Version}\" PrivateAssets=\"All\" />`");
							if (!addin.AnalysisResult.CakeCommonIsPrivate) issuesDescription.AppendLine($"- [ ] The Cake.Common reference should be private. Specifically, your addin's `.csproj` should have a line similar to this: `<PackageReference Include=\"Cake.Common\" Version=\"{recommendedCakeVersion.Version}\" PrivateAssets=\"All\" />`");
							if (!IsFrameworkUpToDate(addin.Frameworks, recommendedCakeVersion.Framework)) issuesDescription.AppendLine($"- [ ] Your addin should target {recommendedCakeVersion.Framework}. Please note that there is no need to multi-target, {recommendedCakeVersion.Framework} is sufficient.");
							if (!addin.AnalysisResult.UsingCakeContribIcon) issuesDescription.AppendLine($"- [ ] The nuget package for your addin should use the cake-contrib icon. Specifically, your addin's `.csproj` should have a line like this: `<PackageIconUrl>{CAKE_CONTRIB_ICON_URL}</PackageIconUrl>`.");

							if (issuesDescription.Length > 0)
							{
								var issueBody = $"We performed an automated audit of your Cake addin and found that it does not follow all the best practices.{Environment.NewLine}{Environment.NewLine}";
								issueBody += $"We encourage you to make the following modifications:{Environment.NewLine}{Environment.NewLine}";
								issueBody += issuesDescription.ToString();
								issueBody += $"{Environment.NewLine}{Environment.NewLine}{Environment.NewLine}";
								issueBody += $"Apologies if this is already being worked on, or if there are existing open issues, this issue was created based on what is currently published for this package on NuGet.org and in the project on github.{Environment.NewLine}";

								var newIssue = new NewIssue(ISSUE_TITLE)
								{
									Body = issueBody.ToString()
								};

								var issue = await _githubClient.Issue.Create(addin.GithubRepoOwner, addin.GithubRepoName, newIssue).ConfigureAwait(false);
								addin.GithubIssueUrl = new Uri(issue.Url);
								addin.GithubIssueId = issue.Number;
							}
						}

						return addin;
					}, MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);

			return addinsMetadata;
		}

		private void GenerateExcelReport(IEnumerable<AddinMetadata> addins)
		{
			if (!_options.ExcelReportToFile && !_options.ExcelReportToRepo) return;

			Console.WriteLine("  Generating Excel report");

			using (var excel = new ExcelPackage(new FileInfo(_excelReportPath)))
			{
				var deprecatedAddins = addins.Where(addin => addin.IsDeprecated).ToArray();
				var auditedAddins = addins.Where(addin => !addin.IsDeprecated && string.IsNullOrEmpty(addin.AnalysisResult.Notes)).ToArray();
				var exceptionAddins = addins.Where(addin => !addin.IsDeprecated && !string.IsNullOrEmpty(addin.AnalysisResult.Notes)).ToArray();

				var namedStyle = excel.Workbook.Styles.CreateNamedStyle("HyperLink");
				namedStyle.Style.Font.UnderLine = true;
				namedStyle.Style.Font.Color.SetColor(Color.Blue);

				// One worksheet per version of Cake
				foreach (var cakeVersion in _cakeVersions.OrderByDescending(cakeVersion => cakeVersion.Version))
				{
					GenerateExcelWorksheet(auditedAddins, cakeVersion, AddinType.Addin | AddinType.Module, $"Cake {cakeVersion.Version}", excel);
				}

				// One worksheet for recipes
				GenerateExcelWorksheet(auditedAddins, null, AddinType.Recipes, "Recipes", excel);

				// Exceptions report
				GenerateExcelWorksheetWithNotes(exceptionAddins, "Exceptions", excel);

				// Deprecated report
				GenerateExcelWorksheetWithNotes(deprecatedAddins, "Deprecated", excel);

				// Save the Excel file
				excel.Save();
			}
		}

		private void GenerateExcelWorksheet(IEnumerable<AddinMetadata> addins, CakeVersion cakeVersion, AddinType type, string caption, ExcelPackage excel)
		{
			var filteredAddins = addins
				.Where(addin => addin.Type.IsFlagSet(type))
				.ToArray();

			var reportColumns = _reportColumns
				.Where(column => column.Destination.HasFlag(DataDestination.Excel))
				.Where(column => column.ApplicableTo.HasFlag(type))
				.Select((data, index) => new { Index = index, Data = data })
				.ToArray();

			// Create the worksheet
			var worksheet = excel.Workbook.Worksheets.Add(caption);

			// Header row
			foreach (var column in reportColumns)
			{
				worksheet.Cells[1, column.Index + 1].Value = column.Data.Header;
			}

			// One row per addin
			var row = 1;
			foreach (var addin in filteredAddins.OrderBy(a => a.Name))
			{
				row++;

				foreach (var column in reportColumns)
				{
					if (column.Data.ApplicableTo.HasFlag(addin.Type))
					{
						var cell = worksheet.Cells[row, column.Index + 1];
						cell.Value = column.Data.GetContent(addin);

						var color = column.Data.GetCellColor(addin, cakeVersion);
						if (color != Color.Empty)
						{
							cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
							cell.Style.Fill.BackgroundColor.SetColor(color);
						}

						var hyperlink = column.Data.GetHyperLink(addin);
						if (hyperlink != null)
						{
							cell.Hyperlink = hyperlink;
							cell.StyleName = "HyperLink";
						}
					}
				}
			}

			// Freeze the top row and setup auto-filter
			worksheet.View.FreezePanes(2, 1);
			worksheet.Cells[1, 1, 1, reportColumns.Length].AutoFilter = true;

			// Format the worksheet
			worksheet.Row(1).Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
			if (filteredAddins.Any())
			{
				foreach (var column in reportColumns)
				{
					worksheet.Cells[2, column.Index + 1, row, column.Index + 1].Style.HorizontalAlignment = column.Data.Align;
				}
			}

			// Resize columns
			worksheet.Cells[1, 1, row, reportColumns.Length].AutoFitColumns();

			// Make columns a little bit wider to account for the filter "drop-down arrow" button
			foreach (var column in reportColumns)
			{
				worksheet.Column(column.Index + 1).Width += 2.14;
			}
		}

		private void GenerateExcelWorksheetWithNotes(IEnumerable<AddinMetadata> addins, string caption, ExcelPackage excel)
		{
			var worksheet = excel.Workbook.Worksheets.Add(caption);

			worksheet.Cells[1, 1].Value = "Addin";
			worksheet.Cells[1, 2].Value = "Notes";

			var row = 1;
			foreach (var addin in addins.OrderBy(p => p.Name))
			{
				row++;
				worksheet.Cells[row, 1].Value = addin.Name;
				worksheet.Cells[row, 2].Value = addin.AnalysisResult.Notes?.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[0] ?? string.Empty;
			}

			// Resize columns and freeze the top row
			worksheet.Cells[1, 1, row, 2].AutoFitColumns();
			worksheet.View.FreezePanes(2, 1);
		}

		private async Task GenerateMarkdownReportAsync(IEnumerable<AddinMetadata> addins)
		{
			if (!_options.MarkdownReportToFile && !_options.MarkdownReportToRepo) return;

			Console.WriteLine("  Generating markdown report");

			var deprecatedAddins = addins.Where(addin => addin.IsDeprecated).ToArray();
			var auditedAddins = addins.Where(addin => !addin.IsDeprecated && string.IsNullOrEmpty(addin.AnalysisResult.Notes)).ToArray();
			var exceptionAddins = addins.Where(addin => !addin.IsDeprecated && !string.IsNullOrEmpty(addin.AnalysisResult.Notes)).ToArray();

			var now = DateTime.UtcNow;
			var markdown = new StringBuilder();

			markdown.AppendLine("# Information");
			markdown.AppendLine();
			markdown.AppendLine($"- This report was generated by Cake.AddinDiscoverer {_addinDiscovererVersion} on {now.ToLongDateString()} at {now.ToLongTimeString()} GMT");
			markdown.AppendLine();

			markdown.AppendLine("# Statistics");
			markdown.AppendLine();
			markdown.AppendLine($"- The analysis discovered {addins.Count()} addins");
			markdown.AppendLine($"  - {auditedAddins.Count()} were successfully audited");
			markdown.AppendLine($"  - {deprecatedAddins.Count()} were marked as deprecated");
			markdown.AppendLine($"  - {exceptionAddins.Count()} could not be audited (see the 'Exceptions' section)");
			markdown.AppendLine();

			markdown.AppendLine($"- Of the {auditedAddins.Count()} audited addins:");
			markdown.AppendLine($"  - {auditedAddins.Count(addin => addin.AnalysisResult.UsingCakeContribIcon)} are using the cake-contrib icon");
			markdown.AppendLine($"  - {auditedAddins.Count(addin => addin.AnalysisResult.TransferedToCakeContribOrganisation)} have been transfered to the cake-contrib organisation");
			markdown.AppendLine();

			markdown.AppendLine("# Reports");
			markdown.AppendLine();
			markdown.AppendLine($"- Click [here]({Path.GetFileNameWithoutExtension(_markdownReportPath)}_for_recipes.md) to view the report for Nuget packages containing recipes.");
			foreach (var cakeVersion in _cakeVersions)
			{
				markdown.AppendLine($"- Click [here]({Path.GetFileNameWithoutExtension(_markdownReportPath)}_for_Cake_{cakeVersion.Version}.md) to view the report for Cake {cakeVersion.Version}.");
			}

			markdown.AppendLine();

			markdown.AppendLine("# Additional audit results");
			markdown.AppendLine();
			markdown.AppendLine("Due to space constraints we couldn't fit all audit information in this report so we generated an Excel spreadsheet that contains the following additional information:");
			markdown.AppendLine("- The `Maintainer` column indicates who is maintaining the source for this project");
			markdown.AppendLine("- The `Icon` column indicates if the nuget package for your addin uses the cake-contrib icon.");
			markdown.AppendLine("- The `Transferred to cake-contrib` column indicates if the project has been moved to the cake-contrib github organisation.");
			markdown.AppendLine();
			markdown.AppendLine("Click [here](Audit.xlsx) to download the Excel spreadsheet.");
			markdown.AppendLine();

			markdown.AppendLine("# Progress");
			markdown.AppendLine();
			markdown.AppendLine("The following graph shows the percentage of addins that are compatible with Cake over time. For the purpose of this graph, we consider an addin to be compatible with a given version of Cake if it references the desired version of Cake.Core and Cake.Common.");
			markdown.AppendLine();
			markdown.AppendLine($"![]({Path.GetFileName(_graphSaveLocation)})");
			markdown.AppendLine();

			// Exceptions report
			markdown.Append(GenerateMarkdownWithNotes(exceptionAddins, "Exceptions"));

			// Deprecated report
			markdown.Append(GenerateMarkdownWithNotes(deprecatedAddins, "Deprecated"));

			// Save
			await File.WriteAllTextAsync(_markdownReportPath, markdown.ToString()).ConfigureAwait(false);

			// Generate the markdown report for nuget packages containing recipes
			var recipesReportName = $"{Path.GetFileNameWithoutExtension(_markdownReportPath)}_for_recipes.md";
			var recipesReportPath = Path.Combine(_tempFolder, recipesReportName);
			var markdownReportForRecipes = GenerateMarkdown(auditedAddins, null, AddinType.Recipes);
			await File.WriteAllTextAsync(recipesReportPath, markdownReportForRecipes).ConfigureAwait(false);

			// Generate the markdown report for each version of Cake
			foreach (var cakeVersion in _cakeVersions)
			{
				var reportName = $"{Path.GetFileNameWithoutExtension(_markdownReportPath)}_for_Cake_{cakeVersion.Version}.md";
				var reportPath = Path.Combine(_tempFolder, reportName);
				var markdownReportForCakeVersion = GenerateMarkdown(auditedAddins, cakeVersion, AddinType.Addin);
				await File.WriteAllTextAsync(reportPath, markdownReportForCakeVersion).ConfigureAwait(false);
			}
		}

		private string GenerateMarkdown(IEnumerable<AddinMetadata> addins, CakeVersion cakeVersion, AddinType type)
		{
			var filteredAddins = addins
				.Where(addin => string.IsNullOrEmpty(addin.AnalysisResult.Notes))
				.Where(addin => addin.Type == type)
				.ToArray();

			var reportColumns = _reportColumns
				.Where(column => column.Destination.HasFlag(GetMarkdownDestinationForType(type)))
				.Where(column => column.ApplicableTo.HasFlag(type))
				.Select((data, index) => new { Index = index, Data = data })
				.ToArray();

			var now = DateTime.UtcNow;
			var markdown = new StringBuilder();

			markdown.AppendLine("# Information");
			markdown.AppendLine();
			markdown.AppendLine($"- This report was generated by Cake.AddinDiscoverer {_addinDiscovererVersion} on {now.ToLongDateString()} at {now.ToLongTimeString()} GMT");
			if (cakeVersion != null)
			{
				markdown.AppendLine($"- The desired Cake version is `{cakeVersion.Version}`");
			}

			if (type == AddinType.Addin)
			{
				markdown.AppendLine("- The `Cake Core Version` and `Cake Common Version` columns  show the version referenced by a given addin");
				markdown.AppendLine($"- The `Cake Core IsPrivate` and `Cake Common IsPrivate` columns indicate whether the references are marked as private. In other words, we are looking for references with the `PrivateAssets=All` attribute like in this example: `<PackageReference Include=\"Cake.Common\" Version=\"{cakeVersion.Version}\" PrivateAssets=\"All\" />`");
				markdown.AppendLine($"- The `Framework` column shows the .NET framework(s) targeted by a given addin. Addins should target {cakeVersion.Framework} only (there is no need to multi-target)");
			}

			markdown.AppendLine();

			if (type == AddinType.Addin)
			{
				markdown.AppendLine("# Statistics");
				markdown.AppendLine();

				var addinsReferencingCakeCore = filteredAddins.Where(addin => addin.Type == AddinType.Addin & addin.AnalysisResult.CakeCoreVersion != null);
				markdown.AppendLine($"- Of the {addinsReferencingCakeCore.Count()} audited addins that reference Cake.Core:");
				markdown.AppendLine($"  - {addinsReferencingCakeCore.Count(addin => IsCakeVersionUpToDate(addin.AnalysisResult.CakeCoreVersion, cakeVersion.Version))} are targeting the desired version of Cake.Core");
				markdown.AppendLine($"  - {addinsReferencingCakeCore.Count(addin => addin.AnalysisResult.CakeCoreIsPrivate)} have marked the reference to Cake.Core as private");
				markdown.AppendLine();

				var addinsReferencingCakeCommon = filteredAddins.Where(addin => addin.Type == AddinType.Addin & addin.AnalysisResult.CakeCommonVersion != null);
				markdown.AppendLine($"- Of the {addinsReferencingCakeCommon.Count()} audited addins that reference Cake.Common:");
				markdown.AppendLine($"  - {addinsReferencingCakeCommon.Count(addin => IsCakeVersionUpToDate(addin.AnalysisResult.CakeCommonVersion, cakeVersion.Version))} are targeting the desired version of Cake.Common");
				markdown.AppendLine($"  - {addinsReferencingCakeCommon.Count(addin => addin.AnalysisResult.CakeCommonIsPrivate)} have marked the reference to Cake.Common as private");
				markdown.AppendLine();
			}

			// Title
			markdown.AppendLine("# Addins");
			markdown.AppendLine();

			// Header row 1
			foreach (var column in reportColumns)
			{
				markdown.Append($"| {column.Data.Header} ");
			}

			markdown.AppendLine("|");

			// Header row 2
			foreach (var column in reportColumns)
			{
				markdown.Append("| ");
				if (column.Data.Align == ExcelHorizontalAlignment.Center) markdown.Append(":");
				markdown.Append("---");
				if (column.Data.Align == ExcelHorizontalAlignment.Right || column.Data.Align == ExcelHorizontalAlignment.Center) markdown.Append(":");
				markdown.Append(" ");
			}

			markdown.AppendLine("|");

			// One row per addin
			foreach (var addin in filteredAddins.OrderBy(addin => addin.Name))
			{
				foreach (var column in reportColumns)
				{
					if (column.Data.ApplicableTo.HasFlag(addin.Type))
					{
						var content = column.Data.GetContent(addin);
						var hyperlink = column.Data.GetHyperLink(addin);
						var color = column.Data.GetCellColor(addin, cakeVersion);

						var emoji = string.Empty;
						if (color == Color.LightGreen) emoji = GREEN_EMOJI;
						else if (color == Color.Red) emoji = RED_EMOJI;

						if (hyperlink == null)
						{
							markdown.Append($"| {content} {emoji}");
						}
						else
						{
							markdown.Append($"| [{content}]({hyperlink.AbsoluteUri}) {emoji}");
						}
					}
					else
					{
						markdown.Append($"| ");
					}
				}

				markdown.AppendLine("|");
			}

			return markdown.ToString();
		}

		private string GenerateMarkdownWithNotes(IEnumerable<AddinMetadata> addins, string title)
		{
			var markdown = new StringBuilder();

			markdown.AppendLine();
			markdown.AppendLine($"# {title}");
			markdown.AppendLine();

			foreach (var addin in addins.OrderBy(p => p.Name))
			{
				markdown.AppendLine($"**{addin.Name}**: {addin.AnalysisResult.Notes?.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[0] ?? string.Empty}{Environment.NewLine}");
			}

			return markdown.ToString();
		}

		/// <summary>
		/// Extract a substring between two markers. For example, Extract("[", "]", "Hello [firstname]") returns "firstname".
		/// </summary>
		/// <param name="startMark">The start marker</param>
		/// <param name="endMark">The end marker</param>
		/// <param name="content">The content</param>
		/// <returns>The substring</returns>
		private string Extract(string startMark, string endMark, string content)
		{
			var start = content.IndexOf(startMark, StringComparison.OrdinalIgnoreCase);
			var end = content.IndexOf(endMark, start + startMark.Length);

			if (start == -1) return string.Empty;
			else if (end == -1) return content.Substring(start + startMark.Length).Trim();
			else return content.Substring(start + startMark.Length, end - start - startMark.Length).Trim();
		}

		private async Task CommitChangesToRepoAsync()
		{
			if (!_options.MarkdownReportToRepo && !_options.ExcelReportToRepo) return;

			Console.WriteLine($"  Committing changes to {CAKE_CONTRIB_REPO_OWNER}/{CAKE_CONTRIB_REPO_NAME} repo");

			// Get the SHA of the latest commit of the master branch.
			var headMasterRef = "heads/master";
			var masterReference = await _githubClient.Git.Reference.Get(CAKE_CONTRIB_REPO_OWNER, CAKE_CONTRIB_REPO_NAME, headMasterRef).ConfigureAwait(false); // Get reference of master branch
			var latestCommit = await _githubClient.Git.Commit.Get(CAKE_CONTRIB_REPO_OWNER, CAKE_CONTRIB_REPO_NAME, masterReference.Object.Sha).ConfigureAwait(false); // Get the laster commit of this branch
			var tree = new NewTree { BaseTree = latestCommit.Tree.Sha };

			// Create the blobs corresponding corresponding to the reports and add them to the tree
			if (_options.ExcelReportToRepo)
			{
				foreach (var excelReport in Directory.EnumerateFiles(_tempFolder, $"*.xlsx"))
				{
					var excelBinary = await File.ReadAllBytesAsync(excelReport).ConfigureAwait(false);
					var excelReportBlob = new NewBlob
					{
						Encoding = EncodingType.Base64,
						Content = Convert.ToBase64String(excelBinary)
					};
					var excelReportBlobRef = await _githubClient.Git.Blob.Create(CAKE_CONTRIB_REPO_OWNER, CAKE_CONTRIB_REPO_NAME, excelReportBlob).ConfigureAwait(false);
					tree.Tree.Add(new NewTreeItem
					{
						Path = Path.GetFileName(excelReport),
						Mode = FILE_MODE,
						Type = TreeType.Blob,
						Sha = excelReportBlobRef.Sha
					});
				}
			}

			if (_options.MarkdownReportToRepo)
			{
				foreach (var markdownReport in Directory.EnumerateFiles(_tempFolder, $"*.md"))
				{
					var makdownReportBlob = new NewBlob
					{
						Encoding = EncodingType.Utf8,
						Content = await File.ReadAllTextAsync(markdownReport).ConfigureAwait(false)
					};
					var makdownReportBlobRef = await _githubClient.Git.Blob.Create(CAKE_CONTRIB_REPO_OWNER, CAKE_CONTRIB_REPO_NAME, makdownReportBlob).ConfigureAwait(false);
					tree.Tree.Add(new NewTreeItem
					{
						Path = Path.GetFileName(markdownReport),
						Mode = FILE_MODE,
						Type = TreeType.Blob,
						Sha = makdownReportBlobRef.Sha
					});
				}
			}

			if (File.Exists(_statsSaveLocation))
			{
				var statsBlob = new NewBlob
				{
					Encoding = EncodingType.Utf8,
					Content = await File.ReadAllTextAsync(_statsSaveLocation).ConfigureAwait(false)
				};
				var statsBlobRef = await _githubClient.Git.Blob.Create(CAKE_CONTRIB_REPO_OWNER, CAKE_CONTRIB_REPO_NAME, statsBlob).ConfigureAwait(false);
				tree.Tree.Add(new NewTreeItem
				{
					Path = Path.GetFileName(_statsSaveLocation),
					Mode = FILE_MODE,
					Type = TreeType.Blob,
					Sha = statsBlobRef.Sha
				});
			}

			if (File.Exists(_graphSaveLocation))
			{
				var graphBinary = await File.ReadAllBytesAsync(_graphSaveLocation).ConfigureAwait(false);
				var graphBlob = new NewBlob
				{
					Encoding = EncodingType.Base64,
					Content = Convert.ToBase64String(graphBinary)
				};
				var graphBlobRef = await _githubClient.Git.Blob.Create(CAKE_CONTRIB_REPO_OWNER, CAKE_CONTRIB_REPO_NAME, graphBlob).ConfigureAwait(false);
				tree.Tree.Add(new NewTreeItem
				{
					Path = Path.GetFileName(_graphSaveLocation),
					Mode = FILE_MODE,
					Type = TreeType.Blob,
					Sha = graphBlobRef.Sha
				});
			}

			// Create a new tree
			var newTree = await _githubClient.Git.Tree.Create(CAKE_CONTRIB_REPO_OWNER, CAKE_CONTRIB_REPO_NAME, tree).ConfigureAwait(false);

			// Create the commit with the SHAs of the tree and the reference of master branch
			var newCommit = new NewCommit($"Automated addins audit {DateTime.UtcNow:yyyy-MM-dd} at {DateTime.UtcNow:HH:mm} UTC", newTree.Sha, masterReference.Object.Sha);
			var commit = await _githubClient.Git.Commit.Create(CAKE_CONTRIB_REPO_OWNER, CAKE_CONTRIB_REPO_NAME, newCommit).ConfigureAwait(false);

			// Update the reference of master branch with the SHA of the commit
			await _githubClient.Git.Reference.Update(CAKE_CONTRIB_REPO_OWNER, CAKE_CONTRIB_REPO_NAME, headMasterRef, new ReferenceUpdate(commit.Sha)).ConfigureAwait(false);
		}

		private async Task<int?> FindCakeRecipeGithubIssueAsync()
		{
			Console.WriteLine("  Finding Cake.Recipe Github issue");

			var request = new RepositoryIssueRequest()
			{
				Creator = _options.GithubUsername,
				State = ItemStateFilter.Open,
				SortProperty = IssueSort.Created,
				SortDirection = SortDirection.Descending
			};

			var issues = await _githubClient.Issue.GetAllForRepository(CAKE_CONTRIB_REPO_OWNER, CAKE_RECIPE_REPO_NAME, request).ConfigureAwait(false);
			var issue = issues.FirstOrDefault(i => i.Title == ISSUE_TITLE);

			return issue?.Number;
		}

		private async Task UpdateCakeRecipeFilesAsync(IEnumerable<AddinMetadata> addins)
		{
			Console.WriteLine("  Updating Cake.Recipe");

			//var owner = "cake-contrib";
			//var repositoryName = "Cake.Recipe";
			var owner = "jericho";
			var repositoryName = "_testing";
			var folderName = "Cake.Recipe/Content";
			var modifiedFiles = new ConcurrentDictionary<string, (IEnumerable<string> AddinNames, string Content)>();
			var regEx = new Regex(@"(?<lineprefix>.*?)(?<packageprefix>\#addin nuget:\?package=)(?<packagename>.*)(?<versionprefix>&version=)(?<packageversion>.*)", RegexOptions.Compiled);

			// --------------------------------------------------
			// STEP 1 - get the list of ".cake" files
			var directoryContent = await _githubClient.Repository.Content.GetAllContents(owner, repositoryName, folderName).ConfigureAwait(false);
			var cakeFiles = directoryContent.Where(c => c.Type == new StringEnum<ContentType>(ContentType.File) && c.Name.EndsWith(".cake", StringComparison.OrdinalIgnoreCase));

			// --------------------------------------------------
			// STEP 2 - update the addin references in the ".cake" files
			await cakeFiles
				.ForEachAsync(
					async cakeFile =>
					{
						var contents = await _githubClient.Repository.Content.GetAllContents(owner, repositoryName, cakeFile.Path).ConfigureAwait(false);
						var fileContent = contents[0].Content;

						var contentModified = false;
						var newFileContent = new StringBuilder();
						var updatedAddinReference = new List<string>();

						foreach (var line in fileContent.Split('\n'))
						{
							var matchResult = regEx.Match(line);
							if (matchResult.Success)
							{
								var packageName = matchResult.Groups["packagename"].Value;
								var packageCurrentVersion = matchResult.Groups["packageversion"].Value;

								var newLine = regEx.Replace(line, m =>
								{
									var package = addins.SingleOrDefault(addin => addin.Name.Equals(m.Groups["packagename"].Value, StringComparison.OrdinalIgnoreCase));

									if (package == null) return m.Groups[0].Value;
									if (m.Groups["packageversion"].Value == package.NugetPackageVersion) return m.Groups[0].Value;

									updatedAddinReference.Add(m.Groups["packagename"].Value);
									return m.Groups["lineprefix"].Value + m.Groups["packageprefix"].Value + m.Groups["packagename"].Value + m.Groups["versionprefix"].Value + package.NugetPackageVersion;
								});

								newFileContent.Append(newLine + '\n');
								contentModified |= newLine != line;
							}
							else
							{
								newFileContent.Append(line + '\n');
							}
						}

						if (contentModified)
						{
							modifiedFiles.TryAdd(cakeFile.Name, (updatedAddinReference.ToArray(), newFileContent.ToString()));
						}
					}, MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);

			if (modifiedFiles.Any())
			{
				// --------------------------------------------------
				// STEP 3 - create issue
				var newIssue = new NewIssue(CAKE_RECIPE_ISSUE_TITLE)
				{
					Body = "The following cake files contain outdated addin references that should be updated:\r\n" +
						string.Join("\r\n", modifiedFiles.Select(f => $"- `{f.Key}` contains the following outdated references:\r\n" +
						string.Join("\r\n", f.Value.AddinNames.Select(n => $"    - {n}"))))
				};
				newIssue.Labels.Add("created-by-addin-discoverer");

				var issue = await _githubClient.Issue.Create(owner, repositoryName, newIssue).ConfigureAwait(false);

				// --------------------------------------------------
				// STEP 4 - commit changes to a new branch
				const string COMMIT_MESSAGE = "Update addins references";
				var developBranchName = "develop";
				var newBranchName = $"update_addins_references_{DateTime.UtcNow:yyyy_MM_dd_HH_mm_ss}";
				var developRef = $"heads/{developBranchName}";
				var newBranchRef = $"heads/{newBranchName}";

				var developReference = await _githubClient.Git.Reference.Get(owner, repositoryName, developRef).ConfigureAwait(false);

				var newReference = new NewReference(newBranchRef, developReference.Object.Sha);
				var newBranch = await _githubClient.Git.Reference.Create(owner, repositoryName, newReference).ConfigureAwait(false);

				var latestCommit = await _githubClient.Git.Commit.Get(owner, repositoryName, newBranch.Object.Sha).ConfigureAwait(false);
				var tree = new NewTree { BaseTree = latestCommit.Tree.Sha };
				foreach (var modifiedFile in modifiedFiles)
				{
					var cakeFileBlob = new NewBlob
					{
						Encoding = EncodingType.Utf8,
						Content = modifiedFile.Value.Content
					};
					var cakeFileBlobRef = await _githubClient.Git.Blob.Create(owner, repositoryName, cakeFileBlob).ConfigureAwait(false);
					tree.Tree.Add(new NewTreeItem
					{
						Path = $"{folderName}/{modifiedFile.Key}",
						Mode = FILE_MODE,
						Type = TreeType.Blob,
						Sha = cakeFileBlobRef.Sha
					});
				}

				var newTree = await _githubClient.Git.Tree.Create(owner, repositoryName, tree).ConfigureAwait(false);

				var newCommit = new NewCommit(COMMIT_MESSAGE, newTree.Sha, newBranch.Object.Sha);
				var commit = await _githubClient.Git.Commit.Create(owner, repositoryName, newCommit).ConfigureAwait(false);

				await _githubClient.Git.Reference.Update(owner, repositoryName, newBranchRef, new ReferenceUpdate(commit.Sha)).ConfigureAwait(false);

				// --------------------------------------------------
				// STEP 5 - submit pull request
				var newPullRequest = new NewPullRequest(COMMIT_MESSAGE, newBranchName, developBranchName)
				{
					Body = $"Resolves #{issue.Number}"
				};
				await _githubClient.PullRequest.Create(owner, repositoryName, newPullRequest).ConfigureAwait(false);
			}
		}

		private async Task UpdateStatsAsync(IEnumerable<AddinMetadata> addins)
		{
			// Do not update the stats if we are only auditing a single addin.
			if (!string.IsNullOrEmpty(_options.AddinName)) return;

			Console.WriteLine("  Updating statistics");

			var content = await _githubClient.Repository.Content.GetAllContents(CAKE_CONTRIB_REPO_OWNER, CAKE_CONTRIB_REPO_NAME, System.IO.Path.GetFileName(_statsSaveLocation)).ConfigureAwait(false);
			File.WriteAllText(_statsSaveLocation, content[0].Content);

			using (var fs = new FileStream(_statsSaveLocation, System.IO.FileMode.Append, FileAccess.Write))
			using (TextWriter writer = new StreamWriter(fs))
			{
				var csv = new CsvWriter(writer);
				csv.Configuration.TypeConverterCache.AddConverter<DateTime>(new DateConverter(CSV_DATE_FORMAT));

				var auditedAddins = addins
					.Where(addin => addin.Type == AddinType.Addin)
					.Where(addin => !addin.IsDeprecated)
					.Where(addin => string.IsNullOrEmpty(addin.AnalysisResult.Notes))
					.ToArray();
				var exceptionAddins = addins
					.Where(addin => addin.Type == AddinType.Addin)
					.Where(addin => !addin.IsDeprecated)
					.Where(addin => !string.IsNullOrEmpty(addin.AnalysisResult.Notes))
					.ToArray();

				foreach (var cakeVersion in _cakeVersions)
				{
					var summary = new AddinProgressSummary
					{
						CakeVersion = cakeVersion.Version.ToString(),
						Date = DateTime.UtcNow,
						CompatibleCount = auditedAddins.Count(addin =>
						{
							return IsCakeVersionUpToDate(addin.AnalysisResult.CakeCoreVersion, cakeVersion.Version) &&
								IsCakeVersionUpToDate(addin.AnalysisResult.CakeCommonVersion, cakeVersion.Version);
						}),
						TotalCount = auditedAddins.Count() + exceptionAddins.Count()
					};

					csv.WriteRecord(summary);
					csv.NextRecord();
				}
			}
		}

		private void GenerateStatsGraph()
		{
			Console.WriteLine("  Generating graph");

			var graphPath = Path.Combine(_tempFolder, "Audit_progress.png");

			var plotModel = new PlotModel
			{
				Title = "Addins compatibility over time",
				Subtitle = "Percentage of all known addins compatible with a given version of Cake"
			};
			var startTime = new DateTime(2018, 3, 21, 0, 0, 0, DateTimeKind.Utc); // We started auditting addins on March 22 2018
			var minDate = DateTimeAxis.ToDouble(startTime);
			var maxDate = minDate + (DateTime.UtcNow - startTime).TotalDays + 2;

			plotModel.Axes.Add(new DateTimeAxis
			{
				Position = AxisPosition.Bottom,
				Minimum = minDate,
				Maximum = maxDate,
				IntervalType = DateTimeIntervalType.Months,
				Title = "Date",
				StringFormat = "yyyy-MM-dd"
			});
			plotModel.Axes.Add(new LinearAxis
			{
				Position = AxisPosition.Left,
				Minimum = 0,
				Maximum = 100,
				MajorStep = 25,
				MinorStep = 5,
				MajorGridlineStyle = LineStyle.Solid,
				MinorGridlineStyle = LineStyle.Dot,
				Title = "Percent"
			});

			using (TextReader reader = new StreamReader(_statsSaveLocation))
			{
				var csv = new CsvReader(reader);
				csv.Configuration.TypeConverterCache.AddConverter<DateTime>(new DateConverter("yyyy-MM-dd HH:mm:ss"));

				var recordsGroupedByCakeVersion = csv
					.GetRecords<AddinProgressSummary>()
					.GroupBy(r => r.CakeVersion);

				foreach (var grp in recordsGroupedByCakeVersion)
				{
					var series = new LineSeries()
					{
						Title = $"Cake {grp.Key}"
					};
					foreach (var statsSummary in grp)
					{
						series.Points.Add(new DataPoint(DateTimeAxis.ToDouble(statsSummary.Date), (Convert.ToDouble(statsSummary.CompatibleCount) / Convert.ToDouble(statsSummary.TotalCount)) * 100));
					}

					plotModel.Series.Add(series);
				}
			}

			var pngExporter = new PngExporter { Width = 600, Height = 400, Background = OxyColors.White };
			pngExporter.ExportToFile(plotModel, graphPath);
		}

		private void EnsureAtLeastOneAddin(IEnumerable<AddinMetadata> normalizedAddins)
		{
			if (!normalizedAddins.Any())
			{
				if (!string.IsNullOrEmpty(_options.AddinName))
				{
					throw new Exception($"Unable to find '{_options.AddinName}'");
				}
				else
				{
					throw new Exception($"Unable to find any addin");
				}
			}
		}
	}
}
