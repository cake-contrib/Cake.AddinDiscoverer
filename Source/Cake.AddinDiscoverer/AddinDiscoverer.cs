using AngleSharp;
using Cake.AddinDiscoverer.Utilities;
using Cake.Common.Solution;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.Incubator;
using CsvHelper;
using Newtonsoft.Json;
using NuGet.Configuration;
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
using System.Text;
using System.Text.RegularExpressions;
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
		private const string NUGET_METADATA_FILE = "nuget.json";
		private const string CSV_DATE_FORMAT = "yyyy-MM-dd HH:mm:ss";

		private readonly Options _options;
		private readonly string _tempFolder;
		private readonly IGitHubClient _githubClient;
		private readonly PackageMetadataResource _nugetPackageMetadataClient;
		private readonly string _jsonSaveLocation;
		private readonly string _statsSaveLocation;
		private readonly string _graphSaveLocation;

		private readonly CakeVersion[] _cakeVersions = new[]
		{
			new CakeVersion { Version = "0.26.0", Framework = "netstandard2.0" },
			new CakeVersion { Version = "0.28.0", Framework = "netstandard2.0" }
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
				(addin) => addin.AnalysisResult.CakeCoreVersion,
				(addin, cakeVersion) => string.IsNullOrEmpty(addin.AnalysisResult.CakeCoreVersion) ? Color.Empty : (IsCakeVersionUpToDate(addin.AnalysisResult.CakeCoreVersion, cakeVersion.Version) ? Color.LightGreen : Color.Red),
				(addin) => null,
				DataDestination.All
			),
			(
				"Cake Core IsPrivate",
				ExcelHorizontalAlignment.Center,
				(addin) => string.IsNullOrEmpty(addin.AnalysisResult.CakeCoreVersion) ? string.Empty : addin.AnalysisResult.CakeCoreIsPrivate.ToString().ToLower(),
				(addin, cakeVersion) => string.IsNullOrEmpty(addin.AnalysisResult.CakeCoreVersion) ? Color.Empty : (addin.AnalysisResult.CakeCoreIsPrivate ? Color.LightGreen : Color.Red),
				(addin) => null,
				DataDestination.All
			),
			(
				"Cake Common Version",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.CakeCommonVersion,
				(addin, cakeVersion) => string.IsNullOrEmpty(addin.AnalysisResult.CakeCommonVersion) ? Color.Empty : (IsCakeVersionUpToDate(addin.AnalysisResult.CakeCommonVersion, cakeVersion.Version) ? Color.LightGreen : Color.Red),
				(addin) => null,
				DataDestination.All
			),
			(
				"Cake Common IsPrivate",
				ExcelHorizontalAlignment.Center,
				(addin) => string.IsNullOrEmpty(addin.AnalysisResult.CakeCommonVersion) ? string.Empty : addin.AnalysisResult.CakeCommonIsPrivate.ToString().ToLower(),
				(addin, cakeVersion) => string.IsNullOrEmpty(addin.AnalysisResult.CakeCommonVersion) ? Color.Empty : (addin.AnalysisResult.CakeCommonIsPrivate ? Color.LightGreen : Color.Red),
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
			_tempFolder = System.IO.Path.Combine(_options.TemporaryFolder, PRODUCT_NAME);
			_jsonSaveLocation = System.IO.Path.Combine(_tempFolder, "CakeAddins.json");
			_statsSaveLocation = System.IO.Path.Combine(_tempFolder, "Audit_stats.csv");
			_graphSaveLocation = System.IO.Path.Combine(_tempFolder, "Audit_progress.png");

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
		}

		public async Task LaunchDiscoveryAsync()
		{
			try
			{
				var excelReportPath = System.IO.Path.Combine(_tempFolder, "Audit.xlsx");
				var markdownReportPath = System.IO.Path.Combine(_tempFolder, "Audit.md");

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

				if (File.Exists(excelReportPath)) File.Delete(excelReportPath);
				if (File.Exists(markdownReportPath)) File.Delete(markdownReportPath);

				foreach (var markdownReport in Directory.EnumerateFiles(_tempFolder, $"{System.IO.Path.GetFileNameWithoutExtension(markdownReportPath)}*.md"))
				{
					File.Delete(markdownReport);
				}

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

				// Reset the summary
				normalizedAddins = ResetSummary(normalizedAddins);
				SaveProgress(normalizedAddins);

				// Get the project URL
				normalizedAddins = await GetProjectUrlAsync(normalizedAddins).ConfigureAwait(false);
				SaveProgress(normalizedAddins);

				// Validate the project URL
				normalizedAddins = await ValidateProjectUrlAsync(normalizedAddins).ConfigureAwait(false);
				SaveProgress(normalizedAddins);

				// Get the path to the .sln file in the github repo
				// Please note: we use the first solution file if there is more than one
				normalizedAddins = await FindSolutionPathAsync(normalizedAddins).ConfigureAwait(false);
				SaveProgress(normalizedAddins);

				// Download a copy of the sln file which simplyfies parsing this file in subsequent steps
				await DownloadSolutionFileAsync(normalizedAddins).ConfigureAwait(false);

				// Get the path to the .csproj file(s)
				normalizedAddins = FindProjectPath(normalizedAddins);
				SaveProgress(normalizedAddins);

				// Download a copy of the csproj file(s) which simplyfies parsing this file in subsequent steps
				await DownloadProjectFilesAsync(normalizedAddins).ConfigureAwait(false);

				// Download package metadata from Nuget.org
				await DownloadNugetMetadataAsync(normalizedAddins).ConfigureAwait(false);

				// Parse the csproj and find all references
				normalizedAddins = FindReferences(normalizedAddins);
				SaveProgress(normalizedAddins);

				// Parse the csproj and find targeted framework(s)
				normalizedAddins = FindFrameworks(normalizedAddins);
				SaveProgress(normalizedAddins);

				// Determine if an issue already exists in the Github repo
				if (_options.CreateGithubIssue)
				{
					normalizedAddins = await FindGithubIssueAsync(normalizedAddins).ConfigureAwait(false);
					SaveProgress(normalizedAddins);
				}

				// Find the nuget metadata such as icon url, package version, etc.
				normalizedAddins = FindNugetMetadata(normalizedAddins);
				SaveProgress(normalizedAddins);

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
				GenerateExcelReport(normalizedAddins, excelReportPath);

				// Generate the markdown report and write to file
				await GenerateMarkdownReportAsync(normalizedAddins, markdownReportPath).ConfigureAwait(false);

				// Update the CSV file containing historical statistics (used to generate graph)
				await UpdateStatsAsync(normalizedAddins).ConfigureAwait(false);

				// Generate the graph showing how many addins are compatible with Cake over time
				GenerateStatsGraph();

				// Commit the changed files (such as reports, stats CSV, graph, etc.) to the cake-contrib repo
				await CommitChangesToRepoAsync().ConfigureAwait(false);
			}
			catch (Exception e)
			{
				Console.WriteLine("\r\n***** AN EXCEPTION HAS OCCURED *****");
				Console.WriteLine(e.Demystify().ToString());
			}
		}

		private static bool IsCakeVersionUpToDate(string currentVersion, string desiredVersion)
		{
			if (string.IsNullOrEmpty(currentVersion)) return true;

			var current = currentVersion.Split('.');
			var desired = desiredVersion.Split('.');

			if (current.Length < desired.Length) return false;

			for (int i = 0; i < desired.Length; i++)
			{
				if (int.Parse(current[i]) < int.Parse(desired[i])) return false;
			}

			return true;
		}

		private static bool IsFrameworkUpToDate(string[] currentFrameworks, string desiredFramework)
		{
			if (currentFrameworks == null) return false;
			else if (currentFrameworks.Length != 1) return false;
			else return currentFrameworks[0].EqualsIgnoreCase(desiredFramework);
		}

		/// <summary>
		/// Sometimes the version has 4 parts (eg: 0.26.0.0) but we only care about the first 3
		/// </summary>
		/// <param name="version">The string version</param>
		/// <returns>The first three parts of a version</returns>
		private static string FormatVersion(string version)
		{
			if (string.IsNullOrEmpty(version)) return UNKNOWN_VERSION;
			return string.Join('.', version.Split('.').Take(3));
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
							addin.AnalysisResult.Notes += $"GetProjectUrlAsync: {e.GetBaseException().Message}\r\n";
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
							addin.AnalysisResult.Notes += $"ValidateProjectUrlAsync: {e.GetBaseException().Message}\r\n";
						}
					}
					return addin;
				});

			return results.ToArray();
		}

		private async Task<AddinMetadata[]> FindSolutionPathAsync(IEnumerable<AddinMetadata> addins)
		{
			Console.WriteLine("  Finding solution files");

			var addinsMetadata = await addins
				.ForEachAsync(
					async addin =>
					{
						try
						{
							if (addin.GithubRepoUrl != null && string.IsNullOrEmpty(addin.SolutionPath))
							{
								var solutionFile = await GetSolutionFileAsync(addin).ConfigureAwait(false);
								addin.SolutionPath = solutionFile.Path;
							}
						}
						catch (NotFoundException)
						{
							addin.AnalysisResult.Notes += $"The project does not exist: {addin.GithubRepoUrl}\r\n";
						}
						catch (Exception e)
						{
							addin.AnalysisResult.Notes += $"FindSolutionPathAsync: {e.GetBaseException().Message}\r\n";
						}

						return addin;
					}, MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);

			return addinsMetadata;
		}

		private AddinMetadata[] FindProjectPath(IEnumerable<AddinMetadata> addins)
		{
			Console.WriteLine("  Finding project files");

			var addinsMetadata = addins
				.Select(addin =>
				{
					if (!string.IsNullOrEmpty(addin.SolutionPath) && addin.ProjectPaths == null)
					{
						try
						{
							var folderLocation = System.IO.Path.Combine(_tempFolder, addin.Name);
							var fileName = System.IO.Path.Combine(folderLocation, System.IO.Path.GetFileName(addin.SolutionPath));
							if (File.Exists(fileName))
							{
								var fileSystem = new FileSystem();
								var cakeEnvironment = new CakeEnvironment(new CakePlatform(), new CakeRuntime(), new NullLog());
								var solutionParser = new SolutionParser(fileSystem, cakeEnvironment);
								var parsedSolution = solutionParser.Parse(fileName);

								if (parsedSolution.Projects != null)
								{
									var solutionParts = addin.SolutionPath.Split('/');

									addin.ProjectPaths = parsedSolution
										.GetProjects()
										.Where(p => !p.Name.EndsWith(".Tests"))
										.Select(p => string.Join('/', solutionParts.Take(solutionParts.Length - 1).Concat(new DirectoryPath(folderLocation).GetRelativePath(p.Path).Segments)))
										.ToArray();
								}
								else
								{
									addin.AnalysisResult.Notes += $"The solution file does not reference any project: {addin.SolutionPath}\r\n";
								}
							}
						}
						catch (Exception e)
						{
							addin.AnalysisResult.Notes += $"FindProjectPath: {e.GetBaseException().Message}\r\n";
						}
					}

					return addin;
				})
				.ToArray();

			return addinsMetadata;
		}

		private async Task DownloadSolutionFileAsync(IEnumerable<AddinMetadata> addins)
		{
			Console.WriteLine("  Downloading solution files");

			await addins
				.ForEachAsync(
					async addin =>
					{
						if (!string.IsNullOrEmpty(addin.SolutionPath))
						{
							var folderLocation = System.IO.Path.Combine(_tempFolder, addin.Name);
							Directory.CreateDirectory(folderLocation);

							try
							{
								var fileName = System.IO.Path.Combine(folderLocation, System.IO.Path.GetFileName(addin.SolutionPath));
								if (!File.Exists(fileName))
								{
									var content = await _githubClient.Repository.Content.GetAllContents(addin.GithubRepoOwner, addin.GithubRepoName, addin.SolutionPath).ConfigureAwait(false);
									File.WriteAllText(fileName, content[0].Content);
								}
							}
							catch (Exception e)
							{
								addin.AnalysisResult.Notes += $"DownloadSolutionFileAsync: {e.GetBaseException().Message}\r\n";
							}
						}
					}, MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);
		}

		private async Task DownloadProjectFilesAsync(IEnumerable<AddinMetadata> addins)
		{
			Console.WriteLine("  Downloading project files");

			await addins
				.ForEachAsync(
					async addin =>
					{
						if (addin.ProjectPaths != null)
						{
							var folderLocation = System.IO.Path.Combine(_tempFolder, addin.Name);
							Directory.CreateDirectory(folderLocation);

							foreach (var projectPath in addin.ProjectPaths)
							{
								try
								{
									var fileName = System.IO.Path.Combine(folderLocation, System.IO.Path.GetFileName(projectPath));
									if (!File.Exists(fileName))
									{
										var content = await _githubClient.Repository.Content.GetAllContents(addin.GithubRepoOwner, addin.GithubRepoName, projectPath).ConfigureAwait(false);
										File.WriteAllText(fileName, content[0].Content);
									}
								}
								catch (Exception e)
								{
									addin.AnalysisResult.Notes += $"DownloadProjectFilesAsync: {e.GetBaseException().Message}\r\n";
								}
							}
						}
					}, MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);
		}

		private async Task DownloadNugetMetadataAsync(IEnumerable<AddinMetadata> addins)
		{
			Console.WriteLine("  Downloading Nuget Metadata");

			var tasks = addins
				.Select(async addin =>
				{
					var folderLocation = System.IO.Path.Combine(_tempFolder, addin.Name);
					Directory.CreateDirectory(folderLocation);

					try
					{
						var fileName = System.IO.Path.Combine(folderLocation, NUGET_METADATA_FILE);
						if (!File.Exists(fileName))
						{
							var searchMetadata = await _nugetPackageMetadataClient.GetMetadataAsync(addin.Name, true, true, new NoopLogger(), CancellationToken.None);
							var mostRecentPackage = searchMetadata.OrderByDescending(p => p.Published).FirstOrDefault();
							if (mostRecentPackage != null)
							{
								var jsonContent = JsonConvert.SerializeObject(mostRecentPackage, Formatting.Indented, new[] { new NuGetVersionConverter() });
								File.WriteAllText(fileName, jsonContent);
							}
						}
					}
					catch (Exception e)
					{
						addin.AnalysisResult.Notes += $"DownloadNugetMetadataAsync: {e.GetBaseException().Message}\r\n";
					}
				});

			await Task.WhenAll(tasks).ConfigureAwait(false);
		}

		private AddinMetadata[] FindReferences(IEnumerable<AddinMetadata> addins)
		{
			Console.WriteLine("  Finding references");

			var results = addins
				.Select(addin =>
				{
					var references = new List<(string Id, string Version, bool IsPrivate)>();
					var folderName = System.IO.Path.Combine(_tempFolder, addin.Name);

					if (Directory.Exists(folderName))
					{
						var csharpProjectFiles = Directory.EnumerateFiles(folderName, "*.csproj");
						var fsharpProjectFiles = Directory.EnumerateFiles(folderName, "*.fsproj");
						var allProjectFiles = csharpProjectFiles.Union(fsharpProjectFiles).ToArray();

						foreach (var projectPath in allProjectFiles)
						{
							try
							{
								references.AddRange(GetProjectReferences(addin, projectPath));
							}
							catch (Exception e)
							{
								addin.AnalysisResult.Notes += $"FindReferences: {e.GetBaseException().Message}\r\n";
							}
						}
					}

					addin.References = references
						.Select(r => new DllReference()
						{
							Id = r.Id,
							Version = r.Version,
							IsPrivate = r.IsPrivate
						})
						.ToArray();

					return addin;
				});

			return results.ToArray();
		}

		private AddinMetadata[] FindFrameworks(IEnumerable<AddinMetadata> addins)
		{
			Console.WriteLine("  Finding Frameworks");

			var results = addins
				.Select(addin =>
				{
					var frameworks = new List<string>();
					var folderName = System.IO.Path.Combine(_tempFolder, addin.Name);

					if (Directory.Exists(folderName))
					{
						foreach (var projectPath in Directory.EnumerateFiles(folderName, "*.csproj"))
						{
							try
							{
								frameworks.AddRange(GetProjectFrameworks(addin, projectPath));
							}
							catch (Exception e)
							{
								addin.AnalysisResult.Notes += $"FindFrameworks: {e.GetBaseException().Message}\r\n";
							}
						}
					}

					addin.Frameworks = frameworks
						.GroupBy(f => f)
						.Select(grp => grp.First())
						.ToArray();

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
								addin.AnalysisResult.Notes += $"FindGithubIssueAsync: {e.GetBaseException().Message}\r\n";
							}
						}

						return addin;
					}, MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);

			return addinsMetadata;
		}

		private AddinMetadata[] FindNugetMetadata(IEnumerable<AddinMetadata> addins)
		{
			Console.WriteLine("  Finding nuget metadata (icon, version, etc.)");

			var results = addins
				.Select(addin =>
				{
					var fileName = System.IO.Path.Combine(_tempFolder, addin.Name, NUGET_METADATA_FILE);

					try
					{
						if (File.Exists(fileName))
						{
							var nugetMetadata = JsonConvert.DeserializeObject<PackageSearchMetadata>(File.ReadAllText(fileName), new[] { new NugetVersionConverter() });
							addin.IconUrl = nugetMetadata.IconUrl;
							addin.NugetPackageVersion = nugetMetadata.Version.ToNormalizedString();
						}
					}
					catch (Exception e)
					{
						addin.AnalysisResult.Notes += $"FindNugetMetadata: {e.GetBaseException().Message}\r\n";
					}

					return addin;
				});

			return results.ToArray();
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
							var cakeCommonVersion = FormatVersion(cakeCommonReference.Min(r => r.Version));
							var cakeCommonIsPrivate = cakeCommonReference.All(r => r.IsPrivate);
							addin.AnalysisResult.CakeCommonVersion = cakeCommonVersion;
							addin.AnalysisResult.CakeCommonIsPrivate = cakeCommonIsPrivate;
						}
						else
						{
							addin.AnalysisResult.CakeCommonVersion = string.Empty;
							addin.AnalysisResult.CakeCommonIsPrivate = true;
						}
						var cakeCoreReference = addin.References.Where(r => r.Id.EqualsIgnoreCase("Cake.Core"));
						if (cakeCoreReference.Any())
						{
							var cakeCoreVersion = FormatVersion(cakeCoreReference.Min(r => r.Version));
							var cakeCoreIsPrivate = cakeCoreReference.All(r => r.IsPrivate);
							addin.AnalysisResult.CakeCoreVersion = cakeCoreVersion;
							addin.AnalysisResult.CakeCoreIsPrivate = cakeCoreIsPrivate;
						}
						else
						{
							addin.AnalysisResult.CakeCoreVersion = string.Empty;
							addin.AnalysisResult.CakeCoreIsPrivate = true;
						}

						addin.AnalysisResult.UsingCakeContribIcon = addin.IconUrl != null && addin.IconUrl.AbsoluteUri.EqualsIgnoreCase(CAKECONTRIB_ICON_URL);
						addin.AnalysisResult.HasYamlFileOnWebSite = addin.Source.HasFlag(AddinMetadataSource.Yaml);
						addin.AnalysisResult.TransferedToCakeContribOrganisation = addin.GithubRepoOwner?.Equals("cake-contrib", StringComparison.OrdinalIgnoreCase) ?? false;
					}

					if (addin.GithubRepoUrl == null)
					{
						addin.AnalysisResult.Notes += "We were unable to determine the Github repo URL. Most likely this means that the PackageProjectUrl is missing from the csproj.\r\n";
					}
					else if (string.IsNullOrEmpty(addin.AnalysisResult.CakeCoreVersion) && string.IsNullOrEmpty(addin.AnalysisResult.CakeCommonVersion))
					{
						addin.AnalysisResult.Notes += "This addin seem to be referencing neither Cake.Core nor Cake.Common.\r\n";
					}

					return addin;
				});

			return results.ToArray();
		}

		private async Task<AddinMetadata[]> CreateGithubIssueAsync(IEnumerable<AddinMetadata> addins)
		{
			Console.WriteLine("  Creating Github issues");

			var recommendedCakeVersion = _cakeVersions
				.OrderByDescending(cakeVersion => Convert.ToInt32(cakeVersion.Version.Split('.')[0]))
				.ThenByDescending(cakeVersion => Convert.ToInt32(cakeVersion.Version.Split('.')[1]))
				.ThenByDescending(cakeVersion => Convert.ToInt32(cakeVersion.Version.Split('.')[2]))
				.First();

			var addinsMetadata = await addins
				.ForEachAsync(
					async addin =>
					{
						if (addin.GithubRepoUrl != null && !addin.GithubIssueId.HasValue)
						{
							var issuesDescription = new StringBuilder();
							if (addin.AnalysisResult.CakeCoreVersion == UNKNOWN_VERSION)
							{
								issuesDescription.AppendLine($"- [ ] We were unable to determine what version of Cake.Core your addin is referencing. Please make sure you are referencing {recommendedCakeVersion.Version}");
							}
							else if (!IsCakeVersionUpToDate(addin.AnalysisResult.CakeCoreVersion, recommendedCakeVersion.Version))
							{
								issuesDescription.AppendLine($"- [ ] You are currently referencing Cake.Core {addin.AnalysisResult.CakeCoreVersion}. Please upgrade to {recommendedCakeVersion.Version}");
							}

							if (addin.AnalysisResult.CakeCommonVersion == UNKNOWN_VERSION)
							{
								issuesDescription.AppendLine($"- [ ] We were unable to determine what version of Cake.Common your addin is referencing. Please make sure you are referencing {recommendedCakeVersion.Version}");
							}
							else if (!IsCakeVersionUpToDate(addin.AnalysisResult.CakeCommonVersion, recommendedCakeVersion.Version))
							{
								issuesDescription.AppendLine($"- [ ] You are currently referencing Cake.Common {addin.AnalysisResult.CakeCommonVersion}. Please upgrade to {recommendedCakeVersion.Version}");
							}

							if (!addin.AnalysisResult.CakeCoreIsPrivate) issuesDescription.AppendLine($"- [ ] The Cake.Core reference should be private. Specifically, your addin's `.csproj` should have a line similar to this: `<PackageReference Include=\"Cake.Core\" Version=\"{recommendedCakeVersion.Version}\" PrivateAssets=\"All\" />`");
							if (!addin.AnalysisResult.CakeCommonIsPrivate) issuesDescription.AppendLine($"- [ ] The Cake.Common reference should be private. Specifically, your addin's `.csproj` should have a line similar to this: `<PackageReference Include=\"Cake.Common\" Version=\"{recommendedCakeVersion.Version}\" PrivateAssets=\"All\" />`");
							if (!IsFrameworkUpToDate(addin.Frameworks, recommendedCakeVersion.Framework)) issuesDescription.AppendLine($"- [ ] Your addin should target {recommendedCakeVersion.Framework}. Please note that there is no need to multi-target, {recommendedCakeVersion.Framework} is sufficient.");
							if (!addin.AnalysisResult.UsingCakeContribIcon) issuesDescription.AppendLine($"- [ ] The nuget package for your addin should use the cake-contrib icon. Specifically, your addin's `.csproj` should have a line like this: `<PackageIconUrl>{CAKECONTRIB_ICON_URL}</PackageIconUrl>`.");
							if (!addin.AnalysisResult.HasYamlFileOnWebSite) issuesDescription.AppendLine("- [ ] There should be a YAML file describing your addin on the cake web site. Specifically, you should add a `.yml` file in this [repo](https://github.com/cake-build/website/tree/develop/addins)");

							if (issuesDescription.Length > 0)
							{
								var issueBody = "We performed an automated audit of your Cake addin and found that it does not follow all the best practices.\r\n\r\n";
								issueBody += "We encourage you to make the following modifications:\r\n\r\n";
								issueBody += issuesDescription.ToString();
								issueBody += "\r\n\r\n\r\nApologies if this is already being worked on, or if there are existing open issues, this issue was created based on what is currently published for this package on NuGet.org and in the project on github.\r\n";

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

		private void GenerateExcelReport(IEnumerable<AddinMetadata> addins, string saveFilePath)
		{
			if (!_options.ExcelReportToFile && !_options.ExcelReportToRepo) return;

			Console.WriteLine("  Generating Excel report");

			var file = new FileInfo(saveFilePath);

			using (var package = new ExcelPackage(file))
			{
				var auditedAddins = addins.Where(addin => string.IsNullOrEmpty(addin.AnalysisResult.Notes));
				var exceptionAddins = addins.Where(addin => !string.IsNullOrEmpty(addin.AnalysisResult.Notes));

				var reportColumns = _reportColumns
					.Where(column => column.destination.HasFlag(DataDestination.Excel))
					.Select((data, index) => new { Index = index, Data = data })
					.ToArray();

				var namedStyle = package.Workbook.Styles.CreateNamedStyle("HyperLink");
				namedStyle.Style.Font.UnderLine = true;
				namedStyle.Style.Font.Color.SetColor(Color.Blue);

				foreach (var cakeVersion in _cakeVersions
					.OrderByDescending(cakeVersion => Convert.ToInt32(cakeVersion.Version.Split('.')[0]))
					.ThenByDescending(cakeVersion => Convert.ToInt32(cakeVersion.Version.Split('.')[1]))
					.ThenByDescending(cakeVersion => Convert.ToInt32(cakeVersion.Version.Split('.')[2])))
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
						worksheet.Cells[row, 2].Value = addin.AnalysisResult.Notes;
					}

					// Resize columns and freeze the top row
					worksheet.Cells[1, 1, row, 2].AutoFitColumns();
					worksheet.View.FreezePanes(2, 1);
				}

				// Save the Excel file
				package.Save();
			}
		}

		private async Task GenerateMarkdownReportAsync(IEnumerable<AddinMetadata> addins, string saveFilePath)
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
				var versionReportName = $"{System.IO.Path.GetFileNameWithoutExtension(saveFilePath)}_for_Cake_{cakeVersion.Version}.md";
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
			markdown.AppendLine("The following graph shows the percentage of addins that are compatible with Cake over time. For the purpose of this graph, we consider an addin to be compatible with a given version of Cake if is references the desired version of Cake.Core and Cake.Common.");
			markdown.AppendLine($"![]({System.IO.Path.GetFileName(_graphSaveLocation)})");
			markdown.AppendLine();

			// Exceptions report
			if (exceptionAddins.Any())
			{
				markdown.AppendLine();
				markdown.AppendLine("# Exceptions");
				markdown.AppendLine();

				foreach (var addin in exceptionAddins.OrderBy(p => p.Name))
				{
					markdown.AppendLine($"**{addin.Name}**: {addin.AnalysisResult.Notes}");
				}
			}

			// Save
			await File.WriteAllTextAsync(saveFilePath, markdown.ToString()).ConfigureAwait(false);

			// Generate the markdown report for each version of Cake
			foreach (var cakeVersion in _cakeVersions)
			{
				var versionReportName = $"{System.IO.Path.GetFileNameWithoutExtension(saveFilePath)}_for_Cake_{cakeVersion.Version}.md";
				var versionReportPath = System.IO.Path.Combine(_tempFolder, versionReportName);

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

				var addinsReferencingCakeCore = auditedAddins.Where(addin => !string.IsNullOrEmpty(addin.AnalysisResult.CakeCoreVersion));
				markdown.AppendLine($"- Of the {addinsReferencingCakeCore.Count()} audited addins that reference Cake.Core:");
				markdown.AppendLine($"  - {addinsReferencingCakeCore.Count(addin => IsCakeVersionUpToDate(addin.AnalysisResult.CakeCoreVersion, cakeVersion.Version))} are targeting the desired version of Cake.Core");
				markdown.AppendLine($"  - {addinsReferencingCakeCore.Count(addin => addin.AnalysisResult.CakeCoreIsPrivate)} have marked the reference to Cake.Core as private");
				markdown.AppendLine();

				var addinsReferencingCakeCommon = auditedAddins.Where(addin => !string.IsNullOrEmpty(addin.AnalysisResult.CakeCommonVersion));
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

		private async Task<RepositoryContent> GetSolutionFileAsync(AddinMetadata addin, string folderName = null)
		{
			var directoryContent = string.IsNullOrEmpty(folderName) ?
					await _githubClient.Repository.Content.GetAllContents(addin.GithubRepoOwner, addin.GithubRepoName).ConfigureAwait(false) :
					await _githubClient.Repository.Content.GetAllContents(addin.GithubRepoOwner, addin.GithubRepoName, folderName).ConfigureAwait(false);

			var solutions = directoryContent.Where(c => c.Type == new StringEnum<ContentType>(ContentType.File) && c.Name.EndsWith(".sln", StringComparison.OrdinalIgnoreCase));
			if (solutions.Any()) return solutions.First();

			var subFolders = directoryContent.Where(c => c.Type == new StringEnum<ContentType>(ContentType.Dir));

			var sourceSubFolders = subFolders.Where(c => c.Name.EqualsIgnoreCase("source") || c.Name.EqualsIgnoreCase("src"));
			if (sourceSubFolders.Any())
			{
				foreach (var subFolder in sourceSubFolders)
				{
					var solutionFile = await GetSolutionFileAsync(addin, subFolder.Name).ConfigureAwait(false);
					if (solutionFile != null) return solutionFile;
				}
			}

			var allOtherSubFolders = subFolders.Except(sourceSubFolders);
			foreach (var subFolder in allOtherSubFolders)
			{
				var solutionFile = await GetSolutionFileAsync(addin, subFolder.Path).ConfigureAwait(false);
				if (solutionFile != null) return solutionFile;
			}

			return null;
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

		private IEnumerable<(string Id, string Version, bool IsPrivate)> GetProjectReferences(AddinMetadata addin, string projectPath)
		{
			var references = new List<(string Id, string Version, bool IsPrivate)>();

			var fileSystem = new FileSystem();
			var projectFile = fileSystem.GetFile(new FilePath(projectPath));
			var parsedProject = projectFile.ParseProjectFile("Release");

			foreach (var reference in parsedProject.References)
			{
				var parts = reference.Include.Split(',', StringSplitOptions.RemoveEmptyEntries);
				var referenceDetails = parts.Skip(1).Select(p => p.Trim().Split('=', StringSplitOptions.RemoveEmptyEntries));

				var id = parts[0];
				if (!string.IsNullOrEmpty(id))
				{
					var version = referenceDetails.FirstOrDefault(d => d[0].EqualsIgnoreCase("Version"))?[1];
					var isPrivate = reference.Private ?? false;
					references.Add((id, version, isPrivate));
				}
			}

			foreach (var reference in parsedProject.PackageReferences)
			{
				var id = reference.Name;
				if (!string.IsNullOrEmpty(id))
				{
					var version = reference.Version;
					var isPrivate = reference.PrivateAssets?.Any(a => a.EqualsIgnoreCase("All")) ?? false;
					references.Add((id, version, isPrivate));
				}
			}

			return references.ToArray();
		}

		private IEnumerable<string> GetProjectFrameworks(AddinMetadata addin, string projectPath)
		{
			var fileSystem = new FileSystem();
			var projectFile = fileSystem.GetFile(new FilePath(projectPath));
			var parsedProject = projectFile.ParseProjectFile("Release");

			return parsedProject.TargetFrameworkVersions;
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

			Console.WriteLine($"  Committing reports to {owner}/{repositoryName} repo");

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
						CakeVersion = cakeVersion.Version,
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

			using (TextReader reader = new StreamReader(System.IO.Path.Combine(_tempFolder, "Audit_stats.csv")))
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

		private async Task GetHistoricalStatsAsync()
		{
			var owner = "cake-contrib";
			var repositoryName = "Home";

			var saveFolder = System.IO.Path.Combine(_tempFolder, "_history");
			if (!Directory.Exists(saveFolder)) Directory.CreateDirectory(saveFolder);

			foreach (var desiredFile in new[] { "Audit.md", "Audit_for_Cake_0.26.0.md", "Audit_for_Cake_0.28.0.md" })
			{
				var commitsForFile = await _githubClient.Repository.Commit.GetAll(owner, repositoryName, new CommitRequest { Path = desiredFile }).ConfigureAwait(false);
				await commitsForFile
					.ForEachAsync(
						async commit =>
						{
							var details = await _githubClient.Repository.Commit.Get(owner, repositoryName, commit.Sha).ConfigureAwait(false);
							var filePath = System.IO.Path.Combine(saveFolder, $"{System.IO.Path.GetFileNameWithoutExtension(desiredFile)}_{details.Commit.Committer.Date.UtcDateTime:yyyy_MM_dd_HH_mm_ss}.md");
							if (!File.Exists(filePath))
							{
								var contents = await _githubClient.Repository.Content.GetAllContentsByRef(owner, repositoryName, desiredFile, commit.Sha).ConfigureAwait(false);
								File.WriteAllText(filePath, contents.First().Content);
							}
						}, MAX_GITHUB_CONCURENCY)
					.ConfigureAwait(false);
			}

			var regEx = new Regex(@"The analysis discovered (?<totalcount>.*?) addins", RegexOptions.Compiled);
			bool IsPositive(string content)
			{
				return string.IsNullOrWhiteSpace(content) ||
					content.Contains("<span style=\"color: green\">") ||
					content.Contains(":white_check_mark:");
			}

			using (TextWriter writer = new StreamWriter(System.IO.Path.Combine(saveFolder, "Audit_stats.csv")))
			{
				var csv = new CsvWriter(writer);
				csv.Configuration.TypeConverterCache.AddConverter<DateTime>(new DateConverter(CSV_DATE_FORMAT));

				csv.WriteHeader<AddinProgressSummary>();
				csv.NextRecord();

				foreach (var filePath in Directory.EnumerateFiles(saveFolder, "Audit_*.md"))
				{
					var file = File.OpenText(filePath);
					var fileContent = await file.ReadToEndAsync().ConfigureAwait(false);
					var fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
					var cakeVersion = fileName.StartsWith("Audit_for_Cake_0.28.0") ? "0.28.0" : "0.26.0";

					var dateParts = fileName
						.Replace("Audit_for_Cake_0.28.0_", string.Empty)
						.Replace("Audit_for_Cake_0.26.0_", string.Empty)
						.Replace("Audit_", string.Empty)
						.Split('_', StringSplitOptions.RemoveEmptyEntries)
						.Select(part => Convert.ToInt32(part))
						.ToArray();
					var date = new DateTime(dateParts[0], dateParts[1], dateParts[2], dateParts[3], dateParts[4], dateParts[5], DateTimeKind.Utc);

					var totalCount = 0;
					var matchResult = regEx.Match(fileContent);
					if (matchResult.Success)
					{
						totalCount = Convert.ToInt32(matchResult.Groups["totalcount"].Value);
					}

					var sectionContent = Extract("# Addins", "#", fileContent);
					var lines = sectionContent.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

					// It's important to skip the two 'header' rows
					var addins = lines
						.Skip(2)
						.Select(line =>
						{
							var cells = line.Split('|', StringSplitOptions.RemoveEmptyEntries);
							return new
							{
								Name = Extract("[", "]", cells[0]),
								CakeCore = IsPositive(cells[1]),
								CakeCommon = IsPositive(cells[3])
							};
						});

					var summary = new AddinProgressSummary
					{
						CakeVersion = cakeVersion,
						Date = date,
						CompatibleCount = addins.Count(a => a.CakeCore && a.CakeCommon),
						TotalCount = totalCount
					};

					csv.WriteRecord(summary);
					csv.NextRecord();
				}
			}
		}
	}
}
