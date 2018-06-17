using AngleSharp;
using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator;
using CsvHelper;
using Newtonsoft.Json;
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
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using YamlDotNet.RepresentationModel;

namespace Cake.AddinDiscoverer
{
	internal class AddinDiscoverer
	{
		private const string FILE_MODE = "100644";
		private const string PRODUCT_NAME = "Cake.AddinDiscoverer";
		private const string ISSUE_TITLE = "Recommended changes resulting from automated audit";
		private const string CAKECONTRIB_ICON_URL = "https://cdn.rawgit.com/cake-contrib/graphics/a5cf0f881c390650144b2243ae551d5b9f836196/png/cake-contrib-medium.png";
		private const string UNKNOWN_VERSION = "Unknown";
		private const int MAX_GITHUB_CONCURENCY = 10;
		private const string GREEN_EMOJI = ":white_check_mark: ";
		private const string RED_EMOJI = ":small_red_triangle: ";
		private const string CSV_DATE_FORMAT = "yyyy-MM-dd HH:mm:ss";

		private static SemVersion _unknownVersion = new SemVersion(0, 0, 0);

		private readonly Options _options;
		private readonly string _tempFolder;
		private readonly string _packagesFolder;
		private readonly string _excelReportPath;
		private readonly string _markdownReportPath;
		private readonly IGitHubClient _githubClient;
		private readonly PackageMetadataResource _nugetPackageMetadataClient;
		private readonly DownloadResource _nugetPackageDownloadClient;
		private readonly string _jsonSaveLocation;
		private readonly string _statsSaveLocation;
		private readonly string _graphSaveLocation;

		private readonly CakeVersion[] _cakeVersions = new[]
		{
			new CakeVersion { Version = new SemVersion(0, 26, 0), Framework = "netstandard2.0" },
			new CakeVersion { Version = new SemVersion(0, 28, 0), Framework = "netstandard2.0" }
		};

#pragma warning disable SA1000 // Keywords should be spaced correctly
#pragma warning disable SA1008 // Opening parenthesis should be spaced correctly
#pragma warning disable SA1009 // Closing parenthesis should be spaced correctly
		private readonly (string Header, ExcelHorizontalAlignment Align, Func<AddinMetadata, string> GetContent, Func<AddinMetadata, CakeVersion, Color> GetCellColor, Func<AddinMetadata, Uri> GetHyperLink, DataDestination destination)[] _reportColumns = new(string Header, ExcelHorizontalAlignment Align, Func<AddinMetadata, string> GetContent, Func<AddinMetadata, CakeVersion, Color> GetCellColor, Func<AddinMetadata, Uri> GetHyperLink, DataDestination Destination)[]
		{
			(
				"Name",
				ExcelHorizontalAlignment.Left,
				(addin) => addin.Name,
				(addin, cakeVersion) => Color.Empty,
				(addin) => addin.GithubRepoUrl ?? addin.NugetPackageUrl,
				DataDestination.All
			),
			(
				"Maintainer",
				ExcelHorizontalAlignment.Left,
				(addin) => addin.Author ?? addin.Maintainer,
				(addin, cakeVersion) => Color.Empty,
				(addin) => null,
				DataDestination.Excel
			),
			(
				"Cake Core Version",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.CakeCoreVersion == null ? string.Empty : addin.AnalysisResult.CakeCoreVersion == _unknownVersion ? UNKNOWN_VERSION : addin.AnalysisResult.CakeCoreVersion.ToString(3),
				(addin, cakeVersion) => addin.AnalysisResult.CakeCoreVersion == null ? Color.Empty : (IsCakeVersionUpToDate(addin.AnalysisResult.CakeCoreVersion, cakeVersion.Version) ? Color.LightGreen : Color.Red),
				(addin) => null,
				DataDestination.All
			),
			(
				"Cake Core IsPrivate",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.CakeCoreVersion == null ? string.Empty : addin.AnalysisResult.CakeCoreIsPrivate.ToString().ToLower(),
				(addin, cakeVersion) => addin.AnalysisResult.CakeCoreVersion == null ? Color.Empty : (addin.AnalysisResult.CakeCoreIsPrivate ? Color.LightGreen : Color.Red),
				(addin) => null,
				DataDestination.All
			),
			(
				"Cake Common Version",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.CakeCommonVersion == null ? string.Empty : addin.AnalysisResult.CakeCommonVersion == _unknownVersion ? UNKNOWN_VERSION : addin.AnalysisResult.CakeCommonVersion.ToString(3),
				(addin, cakeVersion) => addin.AnalysisResult.CakeCommonVersion == null ? Color.Empty : (IsCakeVersionUpToDate(addin.AnalysisResult.CakeCommonVersion, cakeVersion.Version) ? Color.LightGreen : Color.Red),
				(addin) => null,
				DataDestination.All
			),
			(
				"Cake Common IsPrivate",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.CakeCommonVersion == null ? string.Empty : addin.AnalysisResult.CakeCommonIsPrivate.ToString().ToLower(),
				(addin, cakeVersion) => addin.AnalysisResult.CakeCommonVersion == null ? Color.Empty : (addin.AnalysisResult.CakeCommonIsPrivate ? Color.LightGreen : Color.Red),
				(addin) => null,
				DataDestination.All
			),
			(
				"Framework",
				ExcelHorizontalAlignment.Center,
				(addin) => string.Join(", ", addin.Frameworks),
				(addin, cakeVersion) => (addin.Frameworks ?? Array.Empty<string>()).Length == 0 ? Color.Empty : (IsFrameworkUpToDate(addin.Frameworks, cakeVersion.Framework) ? Color.LightGreen : Color.Red),
				(addin) => null,
				DataDestination.All
			),
			(
				"Icon",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.UsingCakeContribIcon.ToString().ToLower(),
				(addin, cakeVersion) => addin.AnalysisResult.UsingCakeContribIcon ? Color.LightGreen : Color.Red,
				(addin) => null,
				DataDestination.Excel
			),
			(
				"YAML",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.HasYamlFileOnWebSite.ToString().ToLower(),
				(addin, cakeVersion) => addin.AnalysisResult.HasYamlFileOnWebSite ? Color.LightGreen : Color.Red,
				(addin) => null,
				DataDestination.Excel
			),
			(
				"Transferred to cake-contrib",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.TransferedToCakeContribOrganisation.ToString().ToLower(),
				(addin, cakeVersion) => addin.AnalysisResult.TransferedToCakeContribOrganisation ? Color.LightGreen : Color.Red,
				(addin) => null,
				DataDestination.Excel
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
			_jsonSaveLocation = Path.Combine(_tempFolder, "CakeAddins.json");
			_statsSaveLocation = Path.Combine(_tempFolder, "Audit_stats.csv");
			_graphSaveLocation = Path.Combine(_tempFolder, "Audit_progress.png");

			// Setup the Github client
			var credentials = new Credentials(_options.GithubUsername, _options.GithuPassword);
			var connection = new Connection(new ProductHeaderValue(PRODUCT_NAME))
			{
				Credentials = credentials,
			};
			_githubClient = new GitHubClient(connection);

			// Setup the Nuget client
			var providers = new List<Lazy<INuGetResourceProvider>>();
			providers.AddRange(NuGet.Protocol.Core.Types.Repository.Provider.GetCoreV3());  // Add v3 API support
			var packageSource = new PackageSource("https://api.nuget.org/v3/index.json");
			var sourceRepository = new SourceRepository(packageSource, providers);
			_nugetPackageMetadataClient = sourceRepository.GetResource<PackageMetadataResource>();
			_nugetPackageDownloadClient = sourceRepository.GetResource<DownloadResource>();
		}

		public async Task LaunchDiscoveryAsync()
		{
			try
			{
				// Clean up
				await Cleanup().ConfigureAwait(false);

				Console.WriteLine("Auditing the Cake Addins");

				var normalizedAddins = File.Exists(_jsonSaveLocation) ?
					JsonConvert.DeserializeObject<AddinMetadata[]>(File.ReadAllText(_jsonSaveLocation)) :
					Enumerable.Empty<AddinMetadata>();

				if (!string.IsNullOrEmpty(_options.AddinName)) normalizedAddins = normalizedAddins.Where(a => a.Name == _options.AddinName);

				if (!normalizedAddins.Any())
				{
					// Discover Cake Addins by going through the '.yml' files in https://github.com/cake-build/website/tree/develop/addins
					var addinsDiscoveredByYaml = await DiscoverCakeAddinsByYmlAsync().ConfigureAwait(false);

					// Discover Cake addins by looking at the "Modules" and "Addins" sections in 'https://raw.githubusercontent.com/cake-contrib/Home/master/Status.md'
					var addinsDiscoveredByWebsiteList = await DiscoverCakeAddinsByWebsiteListAsync().ConfigureAwait(false);

					// Combine all the discovered addins
					normalizedAddins = addinsDiscoveredByYaml
						.Union(addinsDiscoveredByWebsiteList)
						.GroupBy(a => a.Name)
						.Select(grp => new AddinMetadata()
						{
							Name = grp.Key,
							Author = grp.Where(a => a.Author != null).Select(a => a.Author).FirstOrDefault(),
							Maintainer = grp.Where(a => a.Maintainer != null).Select(a => a.Maintainer).FirstOrDefault(),
							GithubRepoUrl = grp.Where(a => a.GithubRepoUrl != null).Select(a => a.GithubRepoUrl).FirstOrDefault(),
							NugetPackageUrl = grp.Where(a => a.NugetPackageUrl != null).Select(a => a.NugetPackageUrl).FirstOrDefault(),
							Source = grp.Select(a => a.Source).Aggregate((x, y) => x | y),
						})
						.ToArray();
				}

				EnsureAtLeastOneAddin(normalizedAddins);

				// Reset the summary
				normalizedAddins = ResetSummary(normalizedAddins);
				SaveProgress(normalizedAddins);

				// Get the project URL
				normalizedAddins = await GetProjectUrlAsync(normalizedAddins).ConfigureAwait(false);
				SaveProgress(normalizedAddins);

				// Validate the project URL
				normalizedAddins = await ValidateProjectUrlAsync(normalizedAddins).ConfigureAwait(false);
				SaveProgress(normalizedAddins);

				// Download package from Nuget.org
				await DownloadNugetPackageAsync(normalizedAddins).ConfigureAwait(false);

				// Determine if an issue already exists in the Github repo
				if (_options.CreateGithubIssue)
				{
					normalizedAddins = await FindGithubIssueAsync(normalizedAddins).ConfigureAwait(false);
					SaveProgress(normalizedAddins);
				}

				// Analyze the nuget metadata
				normalizedAddins = AnalyzeNugetMetadata(normalizedAddins);
				SaveProgress(normalizedAddins);

				// Clean up rejected addins such as addins containing "recipies" for example
				normalizedAddins = normalizedAddins.Where(a => a != null).ToArray();
				EnsureAtLeastOneAddin(normalizedAddins);

				// Analyze
				normalizedAddins = AnalyzeAddin(normalizedAddins);
				SaveProgress(normalizedAddins);

				// Create an issue in the Github repo
				if (_options.CreateGithubIssue)
				{
					normalizedAddins = await CreateGithubIssueAsync(normalizedAddins).ConfigureAwait(false);
					SaveProgress(normalizedAddins);
				}

				// Generate the excel report and save to a file
				GenerateExcelReport(normalizedAddins);

				// Generate the markdown report and write to file
				await GenerateMarkdownReportAsync(normalizedAddins).ConfigureAwait(false);

				// Update the CSV file containing historical statistics (used to generate graph)
				await UpdateStatsAsync(normalizedAddins).ConfigureAwait(false);

				// Generate the graph showing how many addins are compatible with Cake over time
				GenerateStatsGraph();

				// Commit the changed files (such as reports, stats CSV, graph, etc.) to the cake-contrib repo
				await CommitChangesToRepoAsync().ConfigureAwait(false);
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

			foreach (var markdownReport in Directory.EnumerateFiles(_tempFolder, $"{Path.GetFileNameWithoutExtension(_markdownReportPath)}*.md"))
			{
				File.Delete(markdownReport);
			}
		}

		private async Task<AddinMetadata[]> DiscoverCakeAddinsByYmlAsync()
		{
			// Get the list of yaml files in the 'addins' folder
			var directoryContent = await _githubClient.Repository.Content.GetAllContents("cake-build", "website", "addins").ConfigureAwait(false);
			var yamlFiles = directoryContent
				.Where(file => file.Name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
				.Where(file => !string.IsNullOrEmpty(_options.AddinName) ? System.IO.Path.GetFileNameWithoutExtension(file.Name) == _options.AddinName : true)
				.ToArray();

			Console.WriteLine("  Discovering Cake addins by yml");

			var addinsMetadata = await yamlFiles
				.ForEachAsync(
					async file =>
					{
						// Get the content
						var fileWithContent = await _githubClient.Repository.Content.GetAllContents("cake-build", "website", file.Path).ConfigureAwait(false);

						// Parse the content
						var yaml = new YamlStream();
						yaml.Load(new StringReader(fileWithContent[0].Content));

						// Extract Author, Description, Name and repository URL
						var yamlRootNode = (YamlMappingNode)yaml.Documents[0].RootNode;
						var url = new Uri(yamlRootNode.GetChildNodeValue("Repository"));
						var metadata = new AddinMetadata()
						{
							Source = AddinMetadataSource.Yaml,
							Name = yamlRootNode["Name"].ToString(),
							GithubRepoUrl = url.Host.Contains("github.com") ? url : null,
							NugetPackageUrl = url.Host.Contains("nuget.org") ? url : null,
							Author = yamlRootNode.GetChildNodeValue("AuthorGitHubUserName") ?? yamlRootNode.GetChildNodeValue("Author"),
							Maintainer = null
						};

						return metadata;
					}, MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);

			return addinsMetadata;
		}

		private async Task<AddinMetadata[]> DiscoverCakeAddinsByWebsiteListAsync()
		{
			// Get the content of the 'Status.md' file
			var statusFile = await _githubClient.Repository.Content.GetAllContents("cake-contrib", "home", "Status.md").ConfigureAwait(false);
			var statusFileContent = statusFile[0].Content;

			// Get the "modules" and "Addins"
			Console.WriteLine("  Discovering Cake addins by parsing the list in cake-contrib/Home/master/Status.md");

			/*
				The status.md file contains several sections such as "Recipes", "Modules", "Websites", "Addins",
				"Work In Progress", "Needs Investigation" and "Deprecated". I am making the assumption that we
				only care about 2 of those sections: "Modules" and "Addins".
			*/

			var modules = GetAddins("Modules", statusFileContent).ToArray();
			var addins = GetAddins("Addins", statusFileContent).ToArray();

			// Combine the lists
			return modules
				.Union(addins)
				.ToArray();
		}

		private AddinMetadata[] ResetSummary(IEnumerable<AddinMetadata> addins)
		{
			Console.WriteLine("  Clearing previous summary");

			var results = addins
				.Select(addin =>
				{
					addin.AnalysisResult = new AddinAnalysisResult();
					return addin;
				});

			return results.ToArray();
		}

		private async Task<AddinMetadata[]> GetProjectUrlAsync(IEnumerable<AddinMetadata> addins)
		{
			Console.WriteLine("  Getting Github repo URLs");

			var tasks = addins
				.Select(async addin =>
				{
					if (addin.GithubRepoUrl == null && addin.NugetPackageUrl != null)
					{
						try
						{
							addin.GithubRepoUrl = await GetNormalizedProjectUrlAsync(addin.NugetPackageUrl).ConfigureAwait(false);
						}
						catch (Exception e)
						{
							addin.AnalysisResult.Notes += $"GetProjectUrlAsync: {e.GetBaseException().Message}{Environment.NewLine}";
						}
					}
					return addin;
				});

			var results = await Task.WhenAll(tasks).ConfigureAwait(false);
			return results;
		}

		private async Task<AddinMetadata[]> ValidateProjectUrlAsync(IEnumerable<AddinMetadata> addins)
		{
			Console.WriteLine("  Validate Github repo URLs");

			var cakeContribRepositories = await _githubClient.Repository.GetAllForUser("cake-contrib").ConfigureAwait(false);

			var results = addins
				.Select(addin =>
				{
					if (addin.GithubRepoUrl == null ||
						addin.GithubRepoUrl.Host != "github.com" ||
						addin.GithubRepoOwner != "cake-contrib")
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

		private async Task DownloadNugetPackageAsync(IEnumerable<AddinMetadata> addins)
		{
			Console.WriteLine("  Downloading Nuget packages");

			var tasks = addins
				.Select(async addin =>
				{
					try
					{
						var packageFileName = Path.Combine(_packagesFolder, $"{addin.Name}.nupkg");
						if (!File.Exists(packageFileName))
						{
							var searchMetadata = await _nugetPackageMetadataClient.GetMetadataAsync(addin.Name, true, true, NullLogger.Instance, CancellationToken.None).ConfigureAwait(false);
							var mostRecentPackageMetadata = searchMetadata.OrderByDescending(p => p.Published).FirstOrDefault();
							if (mostRecentPackageMetadata == null)
							{
								throw new FileNotFoundException($"Unable to find a package named {addin.Name} on Nuget");
							}
							else
							{
								using (var sourceCacheContext = new SourceCacheContext() { NoCache = true })
								{
									var context = new PackageDownloadContext(sourceCacheContext, Path.GetTempPath(), true);

									using (var result = await _nugetPackageDownloadClient.GetDownloadResourceResultAsync(mostRecentPackageMetadata.Identity, context, string.Empty, NullLogger.Instance, CancellationToken.None))
									{
										if (result.Status == DownloadResourceResultStatus.Cancelled)
										{
											throw new OperationCanceledException();
										}
										if (result.Status == DownloadResourceResultStatus.NotFound)
										{
											throw new Exception(string.Format("Package '{0} {1}' not found", mostRecentPackageMetadata.Identity.Id, mostRecentPackageMetadata.Identity.Version));
										}

										using (var fileStream = File.OpenWrite(packageFileName))
										{
											await result.PackageStream.CopyToAsync(fileStream);
										}
									}
								}
							}
						}
					}
					catch (Exception e)
					{
						addin.AnalysisResult.Notes += $"DownloadNugetPackageAsync: {e.GetBaseException().Message}{Environment.NewLine}";
					}
				});

			await Task.WhenAll(tasks).ConfigureAwait(false);
		}

		private AddinMetadata[] AnalyzeNugetMetadata(IEnumerable<AddinMetadata> addins)
		{
			Console.WriteLine("  Analyzing nuget packages");

			var results = addins
				.Select(addin =>
				{
					try
					{
						var packageFileName = Path.Combine(_packagesFolder, $"{addin.Name}.nupkg");
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
										// Ignore nuget packages that do not contain DLLs (presumably, they contain "recipies" .cake files)
										if (assembliesPath.Length == 0)
										{
											return null;
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

									var allReferences = packageDependencies.Union(assemblyReferences)
										.GroupBy(d => d.Id)
										.Select(grp => new DllReference()
										{
											Id = grp.Key,
											IsPrivate = grp.All(r => r.IsPrivate),
											Version = grp.Min(r => r.Version)
										})
										.ToArray();

									addin.IconUrl = string.IsNullOrEmpty(iconUrl) ? null : new Uri(iconUrl);
									addin.NugetPackageVersion = packageVersion;
									addin.Frameworks = frameworks;
									addin.References = allReferences;
									if (addin.GithubRepoUrl == null) addin.GithubRepoUrl = string.IsNullOrEmpty(projectUrl) ? null : new Uri(projectUrl);
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
								var issue = issues.FirstOrDefault(i => i.Title == ISSUE_TITLE);

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

						addin.AnalysisResult.UsingCakeContribIcon = addin.IconUrl != null && addin.IconUrl.AbsoluteUri.EqualsIgnoreCase(CAKECONTRIB_ICON_URL);
						addin.AnalysisResult.HasYamlFileOnWebSite = addin.Source.HasFlag(AddinMetadataSource.Yaml);
						addin.AnalysisResult.TransferedToCakeContribOrganisation = addin.GithubRepoOwner?.Equals("cake-contrib", StringComparison.OrdinalIgnoreCase) ?? false;
					}

					if (addin.AnalysisResult.CakeCoreVersion == null && addin.AnalysisResult.CakeCommonVersion == null)
					{
						addin.AnalysisResult.Notes += $"This addin seem to be referencing neither Cake.Core nor Cake.Common.{Environment.NewLine}";
					}

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
						if (addin.GithubRepoUrl != null && !addin.GithubIssueId.HasValue)
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
							if (!addin.AnalysisResult.UsingCakeContribIcon) issuesDescription.AppendLine($"- [ ] The nuget package for your addin should use the cake-contrib icon. Specifically, your addin's `.csproj` should have a line like this: `<PackageIconUrl>{CAKECONTRIB_ICON_URL}</PackageIconUrl>`.");
							if (!addin.AnalysisResult.HasYamlFileOnWebSite) issuesDescription.AppendLine("- [ ] There should be a YAML file describing your addin on the cake web site. Specifically, you should add a `.yml` file in this [repo](https://github.com/cake-build/website/tree/develop/addins)");

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

			var file = new FileInfo(_excelReportPath);

			using (var package = new ExcelPackage(file))
			{
				var auditedAddins = addins.Where(addin => string.IsNullOrEmpty(addin.AnalysisResult.Notes)).ToArray();
				var exceptionAddins = addins.Where(addin => !string.IsNullOrEmpty(addin.AnalysisResult.Notes)).ToArray();

				var reportColumns = _reportColumns
					.Where(column => column.destination.HasFlag(DataDestination.Excel))
					.Select((data, index) => new { Index = index, Data = data })
					.ToArray();

				var namedStyle = package.Workbook.Styles.CreateNamedStyle("HyperLink");
				namedStyle.Style.Font.UnderLine = true;
				namedStyle.Style.Font.Color.SetColor(Color.Blue);

				foreach (var cakeVersion in _cakeVersions
					.OrderByDescending(cakeVersion => cakeVersion.Version))
				{
					// One worksheet per version of Cake
					var worksheet = package.Workbook.Worksheets.Add($"Cake {cakeVersion.Version}");

					// Header row
					foreach (var column in reportColumns)
					{
						worksheet.Cells[1, column.Index + 1].Value = column.Data.Header;
					}

					// One row per audited addin
					var row = 1;
					foreach (var addin in auditedAddins.OrderBy(p => p.Name))
					{
						row++;

						foreach (var column in reportColumns)
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

					// Freeze the top row and setup auto-filter
					worksheet.View.FreezePanes(2, 1);
					worksheet.Cells[1, 1, 1, reportColumns.Length].AutoFilter = true;

					// Format the worksheet
					worksheet.Row(1).Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
					if (auditedAddins.Any())
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

				// Exceptions report
				if (exceptionAddins.Any())
				{
					var worksheet = package.Workbook.Worksheets.Add("Exceptions");

					worksheet.Cells[1, 1].Value = "Addin";
					worksheet.Cells[1, 2].Value = "Notes";

					var row = 1;
					foreach (var addin in exceptionAddins.OrderBy(p => p.Name))
					{
						row++;
						worksheet.Cells[row, 1].Value = addin.Name;
						worksheet.Cells[row, 2].Value = addin.AnalysisResult.Notes.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[0];
					}

					// Resize columns and freeze the top row
					worksheet.Cells[1, 1, row, 2].AutoFitColumns();
					worksheet.View.FreezePanes(2, 1);
				}

				// Save the Excel file
				package.Save();
			}
		}

		private async Task GenerateMarkdownReportAsync(IEnumerable<AddinMetadata> addins)
		{
			if (!_options.MarkdownReportToFile && !_options.MarkdownReportToRepo) return;

			Console.WriteLine("  Generating markdown report");

			var auditedAddins = addins.Where(addin => string.IsNullOrEmpty(addin.AnalysisResult.Notes));
			var exceptionAddins = addins.Where(addin => !string.IsNullOrEmpty(addin.AnalysisResult.Notes));

			var reportColumns = _reportColumns
				.Where(column => column.destination.HasFlag(DataDestination.Markdown))
				.Select((data, index) => new { Index = index, Data = data })
				.ToArray();

			var version = string.Empty;
			var assemblyVersion = typeof(AddinDiscoverer).GetTypeInfo().Assembly.GetName().Version;
#if DEBUG
			version = "DEBUG";
#else
			version = $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
#endif

			var now = DateTime.UtcNow;

			var markdown = new StringBuilder();

			markdown.AppendLine("# Information");
			markdown.AppendLine();
			markdown.AppendLine($"- This report was generated by Cake.AddinDiscoverer {version} on {now.ToLongDateString()} at {now.ToLongTimeString()} GMT");
			markdown.AppendLine();

			markdown.AppendLine("# Statistics");
			markdown.AppendLine();
			markdown.AppendLine($"- The analysis discovered {addins.Count()} addins");
			markdown.AppendLine($"  - {auditedAddins.Count()} were successfully audited");
			markdown.AppendLine($"  - {exceptionAddins.Count()} could not be audited (see the 'Exceptions' section)");
			markdown.AppendLine();

			markdown.AppendLine($"- Of the {auditedAddins.Count()} audited addins:");
			markdown.AppendLine($"  - {auditedAddins.Count(addin => addin.AnalysisResult.UsingCakeContribIcon)} are using the cake-contrib icon");
			markdown.AppendLine($"  - {auditedAddins.Count(addin => addin.AnalysisResult.HasYamlFileOnWebSite)} have a YAML file on the cake web site");
			markdown.AppendLine($"  - {auditedAddins.Count(addin => addin.AnalysisResult.TransferedToCakeContribOrganisation)} have been transfered to the cake-contrib organisation");
			markdown.AppendLine();

			markdown.AppendLine("# Reports");
			markdown.AppendLine();
			foreach (var cakeVersion in _cakeVersions)
			{
				var versionReportName = $"{Path.GetFileNameWithoutExtension(_markdownReportPath)}_for_Cake_{cakeVersion.Version}.md";
				markdown.AppendLine($"- Click [here]({versionReportName}) to view the report for Cake {cakeVersion.Version}.");
			}

			markdown.AppendLine();

			markdown.AppendLine("# Additional audit results");
			markdown.AppendLine();
			markdown.AppendLine("Due to space constraints we couldn't fit all audit information in this report so we generated an Excel spreadsheet that contains the following additional information:");
			markdown.AppendLine("- The `Maintainer` column indicates who is maintaining the source for this project");
			markdown.AppendLine("- The `Icon` column indicates if the nuget package for your addin uses the cake-contrib icon.");
			markdown.AppendLine("- The `YAML` column indicates if there is a `.yml` file describing the addin in this [repo](https://github.com/cake-build/website/tree/develop/addins).");
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
			if (exceptionAddins.Any())
			{
				markdown.AppendLine();
				markdown.AppendLine("# Exceptions");
				markdown.AppendLine();

				foreach (var addin in exceptionAddins.OrderBy(p => p.Name))
				{
					markdown.AppendLine($"**{addin.Name}**: {addin.AnalysisResult.Notes.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[0]}{Environment.NewLine}");
				}
			}

			// Save
			await File.WriteAllTextAsync(_markdownReportPath, markdown.ToString()).ConfigureAwait(false);

			// Generate the markdown report for each version of Cake
			foreach (var cakeVersion in _cakeVersions)
			{
				var versionReportName = $"{Path.GetFileNameWithoutExtension(_markdownReportPath)}_for_Cake_{cakeVersion.Version}.md";
				var versionReportPath = Path.Combine(_tempFolder, versionReportName);

				markdown.Clear();

				markdown.AppendLine("# Information");
				markdown.AppendLine();
				markdown.AppendLine($"- This report was generated by Cake.AddinDiscoverer {version} on {now.ToLongDateString()} at {now.ToLongTimeString()} GMT");
				markdown.AppendLine($"- The desired Cake version is `{cakeVersion.Version}`");
				markdown.AppendLine("- The `Cake Core Version` and `Cake Common Version` columns  show the version referenced by a given addin");
				markdown.AppendLine($"- The `Cake Core IsPrivate` and `Cake Common IsPrivate` columns indicate whether the references are marked as private. In other words, we are looking for references with the `PrivateAssets=All` attribute like in this example: `<PackageReference Include=\"Cake.Common\" Version=\"{cakeVersion.Version}\" PrivateAssets=\"All\" />`");
				markdown.AppendLine($"- The `Framework` column shows the .NET framework(s) targeted by a given addin. Addins should target {cakeVersion.Framework} only (there is no need to multi-target)");
				markdown.AppendLine();

				markdown.AppendLine("# Statistics");
				markdown.AppendLine();

				var addinsReferencingCakeCore = auditedAddins.Where(addin => addin.AnalysisResult.CakeCoreVersion != null);
				markdown.AppendLine($"- Of the {addinsReferencingCakeCore.Count()} audited addins that reference Cake.Core:");
				markdown.AppendLine($"  - {addinsReferencingCakeCore.Count(addin => IsCakeVersionUpToDate(addin.AnalysisResult.CakeCoreVersion, cakeVersion.Version))} are targeting the desired version of Cake.Core");
				markdown.AppendLine($"  - {addinsReferencingCakeCore.Count(addin => addin.AnalysisResult.CakeCoreIsPrivate)} have marked the reference to Cake.Core as private");
				markdown.AppendLine();

				var addinsReferencingCakeCommon = auditedAddins.Where(addin => addin.AnalysisResult.CakeCommonVersion != null);
				markdown.AppendLine($"- Of the {addinsReferencingCakeCommon.Count()} audited addins that reference Cake.Common:");
				markdown.AppendLine($"  - {addinsReferencingCakeCommon.Count(addin => IsCakeVersionUpToDate(addin.AnalysisResult.CakeCommonVersion, cakeVersion.Version))} are targeting the desired version of Cake.Common");
				markdown.AppendLine($"  - {addinsReferencingCakeCommon.Count(addin => addin.AnalysisResult.CakeCommonIsPrivate)} have marked the reference to Cake.Common as private");
				markdown.AppendLine();

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
				foreach (var addin in auditedAddins.OrderBy(p => p.Name))
				{
					foreach (var column in reportColumns)
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

					markdown.AppendLine("|");
				}

				// Save
				await File.WriteAllTextAsync(versionReportPath, markdown.ToString()).ConfigureAwait(false);
			}
		}

		/// <summary>
		/// Searches the markdown content for a table between a section title such as '# Modules' and the next section which begins with the '#' character
		/// </summary>
		/// <param name="title">The section title</param>
		/// <param name="content">The markdown content</param>
		/// <returns>An array of addin metadata</returns>
		private AddinMetadata[] GetAddins(string title, string content)
		{
			var sectionContent = Extract($"# {title}", "#", content);
			var lines = sectionContent.Trim('\n').Split('\n', StringSplitOptions.RemoveEmptyEntries);

			Console.WriteLine($"    Discovering {title}");

			// It's important to skip the two 'header' rows
			var results = lines
				.Skip(2)
				.Select(line =>
				{
					var cells = line.Split('|', StringSplitOptions.RemoveEmptyEntries);
					var url = new Uri(Extract("(", ")", cells[0]));
					var metadata = new AddinMetadata()
					{
						Source = AddinMetadataSource.WebsiteList,
						Name = Extract("[", "]", cells[0]),
						GithubRepoUrl = url.Host.Contains("github.com") ? url : null,
						NugetPackageUrl = url.Host.Contains("nuget.org") ? url : null,
						Author = null,
						Maintainer = cells[1].Trim()
					};

					return metadata;
				})
				.Where(addin => !string.IsNullOrEmpty(_options.AddinName) ? System.IO.Path.GetFileNameWithoutExtension(addin.Name) == _options.AddinName : true)
				.ToArray();
			return results;
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

		private async Task<Uri> GetNormalizedProjectUrlAsync(Uri projectUri)
		{
			if (projectUri.Host.Contains("nuget.org"))
			{
				/*
					Fetch the package page from nuget and look for the "Project Site" link.
					Please note that some packages omit this information unfortunately.
				*/

				var config = Configuration.Default.WithDefaultLoader();
				var document = await BrowsingContext.New(config).OpenAsync(Url.Convert(projectUri));

				var outboundProjectUrl = document
					.QuerySelectorAll("a")
					.Where(a =>
					{
						var dataTrackAttrib = a.Attributes["data-track"];
						if (dataTrackAttrib == null) return false;
						return dataTrackAttrib.Value.EqualsIgnoreCase("outbound-project-url");
					});
				if (!outboundProjectUrl.Any()) return null;

				return new Uri(outboundProjectUrl.First().Attributes["href"].Value);
			}
			else
			{
				return projectUri;
			}
		}

		private async Task CommitChangesToRepoAsync()
		{
			if (!_options.MarkdownReportToRepo && !_options.ExcelReportToRepo) return;

			var owner = "cake-contrib";
			var repositoryName = "Home";

			Console.WriteLine($"  Committing changes to {owner}/{repositoryName} repo");

			// Get the SHA of the latest commit of the master branch.
			var headMasterRef = "heads/master";
			var masterReference = await _githubClient.Git.Reference.Get(owner, repositoryName, headMasterRef).ConfigureAwait(false); // Get reference of master branch
			var latestCommit = await _githubClient.Git.Commit.Get(owner, repositoryName, masterReference.Object.Sha).ConfigureAwait(false); // Get the laster commit of this branch
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
					var excelReportBlobRef = await _githubClient.Git.Blob.Create(owner, repositoryName, excelReportBlob).ConfigureAwait(false);
					tree.Tree.Add(new NewTreeItem
					{
						Path = System.IO.Path.GetFileName(excelReport),
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
					var makdownReportBlobRef = await _githubClient.Git.Blob.Create(owner, repositoryName, makdownReportBlob).ConfigureAwait(false);
					tree.Tree.Add(new NewTreeItem
					{
						Path = System.IO.Path.GetFileName(markdownReport),
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
				var statsBlobRef = await _githubClient.Git.Blob.Create(owner, repositoryName, statsBlob).ConfigureAwait(false);
				tree.Tree.Add(new NewTreeItem
				{
					Path = System.IO.Path.GetFileName(_statsSaveLocation),
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
				var graphBlobRef = await _githubClient.Git.Blob.Create(owner, repositoryName, graphBlob).ConfigureAwait(false);
				tree.Tree.Add(new NewTreeItem
				{
					Path = System.IO.Path.GetFileName(_graphSaveLocation),
					Mode = FILE_MODE,
					Type = TreeType.Blob,
					Sha = graphBlobRef.Sha
				});
			}

			// Create a new tree
			var newTree = await _githubClient.Git.Tree.Create(owner, repositoryName, tree).ConfigureAwait(false);

			// Create the commit with the SHAs of the tree and the reference of master branch
			var newCommit = new NewCommit($"Automated addins audit {DateTime.UtcNow:yyyy-MM-dd} at {DateTime.UtcNow:HH:mm} UTC", newTree.Sha, masterReference.Object.Sha);
			var commit = await _githubClient.Git.Commit.Create(owner, repositoryName, newCommit).ConfigureAwait(false);

			// Update the reference of master branch with the SHA of the commit
			// Update HEAD with the commit
			await _githubClient.Git.Reference.Update(owner, repositoryName, headMasterRef, new ReferenceUpdate(commit.Sha)).ConfigureAwait(false);
		}

		private void SaveProgress(IEnumerable<AddinMetadata> normalizedAddins)
		{
			// Do not save progress if we are only auditing a single addin.
			// This is to avoid overwriting the progress file that may have been created by previous audit process.
			if (!string.IsNullOrEmpty(_options.AddinName)) return;

			// Save to file
			File.WriteAllText(_jsonSaveLocation, JsonConvert.SerializeObject(normalizedAddins));
		}

		private async Task UpdateStatsAsync(IEnumerable<AddinMetadata> normalizedAddins)
		{
			// Do not update the stats if we are only auditing a single addin.
			if (!string.IsNullOrEmpty(_options.AddinName)) return;

			var owner = "cake-contrib";
			var repositoryName = "Home";

			Console.WriteLine("  Updating statistics");

			var content = await _githubClient.Repository.Content.GetAllContents(owner, repositoryName, System.IO.Path.GetFileName(_statsSaveLocation)).ConfigureAwait(false);
			File.WriteAllText(_statsSaveLocation, content[0].Content);

			using (var fs = new FileStream(_statsSaveLocation, System.IO.FileMode.Append, FileAccess.Write))
			using (TextWriter writer = new StreamWriter(fs))
			{
				var csv = new CsvWriter(writer);
				csv.Configuration.TypeConverterCache.AddConverter<DateTime>(new DateConverter(CSV_DATE_FORMAT));

				foreach (var cakeVersion in _cakeVersions)
				{
					var summary = new AddinProgressSummary
					{
						CakeVersion = cakeVersion.Version.ToString(),
						Date = DateTime.UtcNow,
						CompatibleCount = normalizedAddins.Count(addin => IsCakeVersionUpToDate(addin.AnalysisResult.CakeCoreVersion, cakeVersion.Version) && IsCakeVersionUpToDate(addin.AnalysisResult.CakeCommonVersion, cakeVersion.Version)),
						TotalCount = normalizedAddins.Count()
					};

					csv.WriteRecord(summary);
					csv.NextRecord();
				}
			}
		}

		private void GenerateStatsGraph()
		{
			Console.WriteLine("  Generating graph");

			var graphPath = System.IO.Path.Combine(_tempFolder, "Audit_progress.png");

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

			using (TextReader reader = new StreamReader(Path.Combine(_tempFolder, "Audit_stats.csv")))
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
