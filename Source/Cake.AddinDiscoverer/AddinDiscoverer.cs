using AngleSharp;
using Cake.Common.Solution;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Core.IO;
using Cake.Incubator;
using Newtonsoft.Json;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using Octokit;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using YamlDotNet.RepresentationModel;

namespace Cake.AddinDiscoverer
{
	internal class AddinDiscoverer
	{
		private const int NUMBER_OF_STEPS = 17;
		private const string PRODUCT_NAME = "Cake.AddinDiscoverer";
		private const string ISSUE_TITLE = "Recommended changes resulting from automated audit";
		private const string CAKECONTRIB_ICON_URL = "https://cdn.rawgit.com/cake-contrib/graphics/a5cf0f881c390650144b2243ae551d5b9f836196/png/cake-contrib-medium.png";
		private const string UNKNOWN_VERSION = "Unknown";
		private const int MAX_GITHUB_CONCURENCY = 10;
		private const string GREEN_EMOJI = ":white_check_mark: ";
		private const string RED_EMOJI = ":small_red_triangle: ";

		private readonly Options _options;
		private readonly string _tempFolder;
		private readonly IGitHubClient _githubClient;
		private readonly PackageMetadataResource _nugetPackageMetadataClient;

#pragma warning disable SA1000 // Keywords should be spaced correctly
#pragma warning disable SA1008 // Opening parenthesis should be spaced correctly
#pragma warning disable SA1009 // Closing parenthesis should be spaced correctly
		private readonly (string Header, ExcelHorizontalAlignment Align, Func<AddinMetadata, string> GetContent, Func<AddinMetadata, Color> GetCellColor)[] _reportColumns = new(string Header, ExcelHorizontalAlignment Align, Func<AddinMetadata, string> GetContent, Func<AddinMetadata, Color> GetCellColor)[]
		{
			(
				"Name",
				ExcelHorizontalAlignment.Left,
				(addin) => $"[{addin.Name}]({addin.GithubRepoUrl?.AbsoluteUri ?? addin.NugetPackageUrl?.AbsoluteUri ?? string.Empty})",
				(addin) => Color.Empty
			),
			(
				"Cake Core Version",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.CakeCoreVersion,
				(addin) => string.IsNullOrEmpty(addin.AnalysisResult.CakeCoreVersion) ? Color.Empty : (addin.AnalysisResult.CakeCoreIsUpToDate ? Color.LightGreen : Color.Red)
			),
			(
				"Cake Core IsPrivate",
				ExcelHorizontalAlignment.Center,
				(addin) => string.IsNullOrEmpty(addin.AnalysisResult.CakeCoreVersion) ? string.Empty : addin.AnalysisResult.CakeCoreIsPrivate.ToString().ToLower(),
				(addin) => string.IsNullOrEmpty(addin.AnalysisResult.CakeCoreVersion) ? Color.Empty : (addin.AnalysisResult.CakeCoreIsPrivate ? Color.LightGreen : Color.Red)
			),
			(
				"Cake Common Version",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.CakeCommonVersion,
				(addin) => string.IsNullOrEmpty(addin.AnalysisResult.CakeCommonVersion) ? Color.Empty : (addin.AnalysisResult.CakeCommonIsUpToDate ? Color.LightGreen : Color.Red)
			),
			(
				"Cake Common IsPrivate",
				ExcelHorizontalAlignment.Center,
				(addin) => string.IsNullOrEmpty(addin.AnalysisResult.CakeCommonVersion) ? string.Empty : addin.AnalysisResult.CakeCommonIsPrivate.ToString().ToLower(),
				(addin) => string.IsNullOrEmpty(addin.AnalysisResult.CakeCommonVersion) ? Color.Empty : (addin.AnalysisResult.CakeCommonIsPrivate ? Color.LightGreen : Color.Red)
			),
			(
				"Framework",
				ExcelHorizontalAlignment.Center,
				(addin) => string.Join(", ", addin.Frameworks),
				(addin) => (addin.Frameworks ?? Array.Empty<string>()).Length == 0 ? Color.Empty : (addin.AnalysisResult.TargetsExpectedFramework ? Color.LightGreen : Color.Red)
			),
			(
				"Icon",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.UsingCakeContribIcon.ToString().ToLower(),
				(addin) => addin.AnalysisResult.UsingCakeContribIcon ? Color.LightGreen : Color.Red
			),
			(
				"YAML",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.HasYamlFileOnWebSite.ToString().ToLower(),
				(addin) => addin.AnalysisResult.HasYamlFileOnWebSite ? Color.LightGreen : Color.Red
			),
		};
#pragma warning restore SA1009 // Closing parenthesis should be spaced correctly
#pragma warning restore SA1008 // Opening parenthesis should be spaced correctly
#pragma warning restore SA1000 // Keywords should be spaced correctly

		public AddinDiscoverer(Options options)
		{
			_options = options;
			_tempFolder = System.IO.Path.Combine(_options.TemporaryFolder, PRODUCT_NAME);

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
				if (_options.ClearCache && Directory.Exists(_tempFolder))
				{
					Directory.Delete(_tempFolder, true);
					await Task.Delay(1000).ConfigureAwait(false);
				}

				if (!Directory.Exists(_tempFolder))
				{
					Directory.CreateDirectory(_tempFolder);
					await Task.Delay(1000).ConfigureAwait(false);
				}

				Console.WriteLine("Auditing the Cake Addins");

				var jsonSaveLocation = System.IO.Path.Combine(_tempFolder, "CakeAddins.json");

				var normalizedAddins = File.Exists(jsonSaveLocation) ?
					JsonConvert.DeserializeObject<AddinMetadata[]>(File.ReadAllText(jsonSaveLocation)) :
					Enumerable.Empty<AddinMetadata>();

				if (!normalizedAddins.Any())
				{
					// Step 1 - Discover Cake Addins by going through the '.yml' files in https://github.com/cake-build/website/tree/develop/addins
					var addinsDiscoveredByYaml = await DiscoverCakeAddinsByYmlAsync().ConfigureAwait(false);

					// Step 2 - Discover Cake addins by looking at the "Recipe", "Modules" and "Addins" section in 'https://raw.githubusercontent.com/cake-contrib/Home/master/Status.md'
					var addinsDiscoveredByWebsiteList = await DiscoverCakeAddinsByWebsiteList().ConfigureAwait(false);

					// Combine all the discovered addins
					normalizedAddins = addinsDiscoveredByYaml
						.Union(addinsDiscoveredByWebsiteList)
						.GroupBy(a => a.Name)
						.Select(grp => new AddinMetadata()
						{
							Name = grp.Key,
							Maintainer = grp.Where(a => a.Maintainer != null).Select(a => a.Maintainer).FirstOrDefault(),
							GithubRepoUrl = grp.Where(a => a.GithubRepoUrl != null).Select(a => a.GithubRepoUrl).FirstOrDefault(),
							NugetPackageUrl = grp.Where(a => a.NugetPackageUrl != null).Select(a => a.NugetPackageUrl).FirstOrDefault(),
							Source = grp.Select(a => a.Source).Aggregate((x, y) => x | y),
						})
						.ToArray();
				}

				// Step 3 - reset the summary
				normalizedAddins = ResetSummaryAsync(normalizedAddins);
				File.WriteAllText(jsonSaveLocation, JsonConvert.SerializeObject(normalizedAddins));

				// Step 4 - get the project URL
				normalizedAddins = await GetProjectUrlAsync(normalizedAddins).ConfigureAwait(false);
				File.WriteAllText(jsonSaveLocation, JsonConvert.SerializeObject(normalizedAddins));

				// Step 5 - get the path to the .sln file in the github repo
				// Please note: we use the first solution file if there is more than one
				normalizedAddins = await FindSolutionPathAsync(normalizedAddins).ConfigureAwait(false);
				File.WriteAllText(jsonSaveLocation, JsonConvert.SerializeObject(normalizedAddins));

				// Step 6 - download a copy of the sln file which simplyfies parsing this file in subsequent steps
				await DownloadSolutionFileAsync(normalizedAddins).ConfigureAwait(false);

				// Step 7 - get the path to the .csproj file(s)
				normalizedAddins = FindProjectPathAsync(normalizedAddins);
				File.WriteAllText(jsonSaveLocation, JsonConvert.SerializeObject(normalizedAddins));

				// Step 8 - download a copy of the csproj file(s) which simplyfies parsing this file in subsequent steps
				await DownloadProjectFilesAsync(normalizedAddins).ConfigureAwait(false);

				// Step 9 - download package metadata from Nuget.org
				await DownloadNugetMetadataAsync(normalizedAddins).ConfigureAwait(false);

				// Step 10 - parse the csproj and find all references
				normalizedAddins = FindReferencesAsync(normalizedAddins);
				File.WriteAllText(jsonSaveLocation, JsonConvert.SerializeObject(normalizedAddins));

				// Step 11 - parse the csproj and find targeted framework(s)
				normalizedAddins = FindFrameworksAsync(normalizedAddins);
				File.WriteAllText(jsonSaveLocation, JsonConvert.SerializeObject(normalizedAddins));

				// Step 12 - determine if an issue already exists in the Github repo
				if (_options.CreateGithubIssue)
				{
					normalizedAddins = await FindGithubIssueAsync(normalizedAddins).ConfigureAwait(false);
					File.WriteAllText(jsonSaveLocation, JsonConvert.SerializeObject(normalizedAddins));
				}

				// Step 13 - find the addin icon
				normalizedAddins = FindIconAsync(normalizedAddins);
				File.WriteAllText(jsonSaveLocation, JsonConvert.SerializeObject(normalizedAddins));

				// Step 14 - analyze
				normalizedAddins = AnalyzeAddinAsync(normalizedAddins);
				File.WriteAllText(jsonSaveLocation, JsonConvert.SerializeObject(normalizedAddins));

				// Step 15 - create an issue in the Github repo
				if (_options.CreateGithubIssue)
				{
					normalizedAddins = await CreateGithubIssueAsync(normalizedAddins).ConfigureAwait(false);
					File.WriteAllText(jsonSaveLocation, JsonConvert.SerializeObject(normalizedAddins));
				}

				// Step 16 - generate the excel report
				if (_options.GenerateExcelReport) GenerateExcelReport(normalizedAddins);

				// Step 17 - generate the markdown report and write to file and/or commit to cake-contrib repo
				var markdownReport = (string)null;
				if (_options.MarkdownReportToFile || _options.MarkdownReportToRepo) markdownReport = GenerateMarkdownReport(normalizedAddins);

				if (_options.MarkdownReportToFile)
				{
					var reportSaveLocation = System.IO.Path.Combine(_tempFolder, "AddinDiscoveryReport.md");
					await File.WriteAllTextAsync(reportSaveLocation, markdownReport).ConfigureAwait(false);
				}

				if (_options.MarkdownReportToRepo) await CommitMarkdownReportToRepoAsync(markdownReport).ConfigureAwait(false);
			}
			catch (Exception e)
			{
				Console.WriteLine("\r\n***** AN EXCEPTION HAS OCCURED *****");
				Console.WriteLine(e.Demystify().ToString());
			}
		}

		private static bool IsUpToDate(string currentVersion, string desiredVersion)
		{
			if (string.IsNullOrEmpty(currentVersion)) return false;

			var current = currentVersion.Split('.');
			var desired = desiredVersion.Split('.');

			if (current.Length < desired.Length) return false;

			for (int i = 0; i < desired.Length; i++)
			{
				if (int.Parse(current[i]) < int.Parse(desired[i])) return false;
			}

			return true;
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
				.Where(c => c.Name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
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
						var yamlRootNode = yaml.Documents[0].RootNode;
						var url = new Uri(yamlRootNode["Repository"].ToString());
						var metadata = new AddinMetadata()
						{
							Source = AddinMetadataSource.Yaml,
							Name = yamlRootNode["Name"].ToString(),
							GithubRepoUrl = url.Host.Contains("github.com") ? url : null,
							NugetPackageUrl = url.Host.Contains("nuget.org") ? url : null,
							Maintainer = yamlRootNode["Author"].ToString().Trim(),
						};

						return metadata;
					}, MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);

			return addinsMetadata;
		}

		private async Task<AddinMetadata[]> DiscoverCakeAddinsByWebsiteList()
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

		private AddinMetadata[] ResetSummaryAsync(IEnumerable<AddinMetadata> addins)
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
							addin.GithubRepoUrl = await GetNormalizedProjectUrl(addin.NugetPackageUrl).ConfigureAwait(false);
						}
						catch (Exception e)
						{
							addin.AnalysisResult.Notes += $"GetProjectUrlAsync: {e.GetBaseException().Message}\r\n";
						}
					}
					return addin;
				});

			var results = await Task.WhenAll(tasks).ConfigureAwait(false);
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

		private AddinMetadata[] FindProjectPathAsync(IEnumerable<AddinMetadata> addins)
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
										.Select(p => string.Join('/', solutionParts.Take(solutionParts.Length - 1).Union(new DirectoryPath(folderLocation).GetRelativePath(p.Path).Segments)))
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
							addin.AnalysisResult.Notes += $"FindProjectPathAsync: {e.GetBaseException().Message}\r\n";
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
						var fileName = System.IO.Path.Combine(folderLocation, "nuget.json");
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

		private AddinMetadata[] FindReferencesAsync(IEnumerable<AddinMetadata> addins)
		{
			Console.WriteLine("  Finding references");

			var results = addins
				.Select(addin =>
				{
					var references = new List<(string Id, string Version, bool IsPrivate)>();
					var folderName = System.IO.Path.Combine(_tempFolder, addin.Name);

					if (Directory.Exists(folderName))
					{
						foreach (var projectPath in Directory.EnumerateFiles(folderName, "*.csproj"))
						{
							try
							{
								references.AddRange(GetProjectReferencesAsync(addin, projectPath));
							}
							catch (Exception e)
							{
								addin.AnalysisResult.Notes += $"FindReferencesAsync: {e.GetBaseException().Message}\r\n";
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

		private AddinMetadata[] FindFrameworksAsync(IEnumerable<AddinMetadata> addins)
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
								frameworks.AddRange(GetProjectFrameworksAsync(addin, projectPath));
							}
							catch (Exception e)
							{
								addin.AnalysisResult.Notes += $"FindFrameworksAsync: {e.GetBaseException().Message}\r\n";
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
			Console.Write("  Finding Github issues");
			var tasks = addins
				.Select(async addin =>
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
				});

			var results = await Task.WhenAll(tasks).ConfigureAwait(false);
			return results.ToArray();
		}

		private AddinMetadata[] FindIconAsync(IEnumerable<AddinMetadata> addins)
		{
			Console.WriteLine("  Finding icons");

			var results = addins
				.Select(addin =>
				{
					var fileName = System.IO.Path.Combine(_tempFolder, addin.Name, "nuget.json");

					try
					{
						if (File.Exists(fileName))
						{
							var nugetMetadata = JsonConvert.DeserializeObject<PackageSearchMetadata>(File.ReadAllText(fileName), new[] { new NugetVersionConverter() });
							addin.IconUrl = nugetMetadata.IconUrl;
						}
					}
					catch (Exception e)
					{
						addin.AnalysisResult.Notes += $"FindIconAsync: {e.GetBaseException().Message}\r\n";
					}

					return addin;
				});

			return results.ToArray();
		}

		private AddinMetadata[] AnalyzeAddinAsync(IEnumerable<AddinMetadata> addins)
		{
			Console.WriteLine("  Analyzing addins");

			var results = addins
				.Select(addin =>
				{
					addin.AnalysisResult.TargetsExpectedFramework =
						(addin.Frameworks ?? Array.Empty<string>()).Length == 1 &&
						addin.Frameworks[0].EqualsIgnoreCase("netstandard2.0");

					if (addin.References != null)
					{
						var cakeCommonReference = addin.References.Where(r => r.Id.EqualsIgnoreCase("Cake.Common"));
						if (cakeCommonReference.Any())
						{
							var cakeCommonVersion = FormatVersion(cakeCommonReference.Min(r => r.Version));
							var cakeCommonIsPrivate = cakeCommonReference.All(r => r.IsPrivate);
							addin.AnalysisResult.CakeCommonVersion = FormatVersion(cakeCommonReference.Min(r => r.Version));
							addin.AnalysisResult.CakeCommonIsPrivate = cakeCommonReference.All(r => r.IsPrivate);
							addin.AnalysisResult.CakeCommonIsUpToDate = IsUpToDate(cakeCommonVersion, _options.RecommendedCakeVersion);
						}
						else
						{
							addin.AnalysisResult.CakeCommonVersion = string.Empty;
							addin.AnalysisResult.CakeCommonIsPrivate = true;
							addin.AnalysisResult.CakeCommonIsUpToDate = true;
						}
						var cakeCoreReference = addin.References.Where(r => r.Id.EqualsIgnoreCase("Cake.Core"));
						if (cakeCoreReference.Any())
						{
							var cakeCoreVersion = FormatVersion(cakeCoreReference.Min(r => r.Version));
							var cakeCoreIsPrivate = cakeCoreReference.All(r => r.IsPrivate);
							addin.AnalysisResult.CakeCoreVersion = cakeCoreVersion;
							addin.AnalysisResult.CakeCoreIsPrivate = cakeCoreIsPrivate;
							addin.AnalysisResult.CakeCoreIsUpToDate = IsUpToDate(cakeCoreVersion, _options.RecommendedCakeVersion);
						}
						else
						{
							addin.AnalysisResult.CakeCoreVersion = string.Empty;
							addin.AnalysisResult.CakeCoreIsPrivate = true;
							addin.AnalysisResult.CakeCoreIsUpToDate = true;
						}

						addin.AnalysisResult.UsingCakeContribIcon = addin.IconUrl != null && addin.IconUrl.AbsoluteUri.EqualsIgnoreCase(CAKECONTRIB_ICON_URL);
						addin.AnalysisResult.HasYamlFileOnWebSite = addin.Source.HasFlag(AddinMetadataSource.Yaml);
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

			var tasks = addins
				.Select(async addin =>
				{
					if (addin.GithubRepoUrl != null && addin.GithubIssueUrl == null)
					{
						var issuesDescription = new StringBuilder();
						if (!string.IsNullOrEmpty(addin.AnalysisResult.CakeCoreVersion) &&
							addin.AnalysisResult.CakeCoreVersion != UNKNOWN_VERSION &&
							!addin.AnalysisResult.CakeCoreIsUpToDate)
						{
							issuesDescription.Append($"- [ ] You are currently referencing Cake.Core {addin.AnalysisResult.CakeCoreVersion}. Please upgrade to {_options.RecommendedCakeVersion}\r\n");
						}
						if (!string.IsNullOrEmpty(addin.AnalysisResult.CakeCommonVersion) &&
							addin.AnalysisResult.CakeCommonVersion != UNKNOWN_VERSION &&
							!addin.AnalysisResult.CakeCommonIsUpToDate)
						{
							issuesDescription.Append($"- [ ] You are currently referencing Cake.Common {addin.AnalysisResult.CakeCommonVersion}. Please upgrade to {_options.RecommendedCakeVersion}\r\n");
						}
						if (!addin.AnalysisResult.CakeCoreIsPrivate) issuesDescription.Append($"- [ ] The Cake.Core reference should be private.\r\nSpecifically, your addin's `.csproj` should have a line similar to this:\r\n`<PackageReference Include=\"Cake.Core\" Version=\"{_options.RecommendedCakeVersion}\" PrivateAssets=\"All\" />`");
						if (!addin.AnalysisResult.CakeCommonIsPrivate) issuesDescription.Append($"- [ ] The Cake.Common reference should be private.\r\nSpecifically, your addin's `.csproj` should have a line similar to this:\r\n`<PackageReference Include=\"Cake.Common\" Version=\"{_options.RecommendedCakeVersion}\" PrivateAssets=\"All\" />`");
						if (!addin.AnalysisResult.TargetsExpectedFramework) issuesDescription.Append("- [ ] Your addin should target netstandard2.0\r\nPlease note that there is no need to multi-target: as of Cake 0.26.0, netstandard2.0 is sufficient.\r\n");
						if (!addin.AnalysisResult.UsingCakeContribIcon) issuesDescription.Append($"- [ ] The nuget package for your addin should use the cake-contrib icon.\r\nSpecifically, your addin's `.csproj` should have a line like this: `<PackageIconUrl>{CAKECONTRIB_ICON_URL}</PackageIconUrl>`.\r\n");
						if (!addin.AnalysisResult.HasYamlFileOnWebSite) issuesDescription.Append("- [ ] There should be a YAML file describing your addin on the cake web site\r\nSpecifically, you should add a `.yml` file in this [repo](https://github.com/cake-build/website/tree/develop/addins)");

						if (issuesDescription.Length > 0)
						{
							var issueBody = "We performed an automated audit of your Cake addin and found that it does not follow all the best practices.\r\n\r\n";
							issueBody += "We encourage you to make the following modifications:\r\n\r\n";
							issueBody += issuesDescription.ToString();

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
				});

			var results = await Task.WhenAll(tasks).ConfigureAwait(false);
			return results.ToArray();
		}

		private void GenerateExcelReport(IEnumerable<AddinMetadata> addins)
		{
			Console.WriteLine("  Generating Excel report");

			var reportSaveLocation = System.IO.Path.Combine(_tempFolder, "AddinDiscoveryReport.xlsx");

			FileInfo file = new FileInfo(reportSaveLocation);

			if (file.Exists)
			{
				file.Delete();
				file = new FileInfo(reportSaveLocation);
			}

			using (var package = new ExcelPackage(file))
			{
				var auditedAddins = addins.Where(addin => string.IsNullOrEmpty(addin.AnalysisResult.Notes));
				var exceptionAddins = addins.Where(addin => !string.IsNullOrEmpty(addin.AnalysisResult.Notes));

				var namedStyle = package.Workbook.Styles.CreateNamedStyle("HyperLink");
				namedStyle.Style.Font.UnderLine = true;
				namedStyle.Style.Font.Color.SetColor(Color.Blue);

				var worksheet = package.Workbook.Worksheets.Add("Addins");

				// Header row
				for (int i = 0; i < _reportColumns.Length; i++)
				{
					worksheet.Cells[1, i + 1].Value = _reportColumns[i].Header;
				}

				// One row per audited addin
				var row = 1;
				foreach (var addin in auditedAddins.OrderBy(p => p.Name))
				{
					row++;
					worksheet.Cells[row, 1].Value = addin.Name;
					worksheet.Cells[row, 1].Hyperlink = addin.GithubRepoUrl ?? addin.NugetPackageUrl;
					worksheet.Cells[row, 1].StyleName = "HyperLink";

					for (int i = 1; i < _reportColumns.Length; i++)
					{
						var cell = worksheet.Cells[row, i + 1];
						cell.Value = _reportColumns[i].GetContent(addin);

						var color = _reportColumns[i].GetCellColor(addin);
						if (color != Color.Empty)
						{
							cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
							cell.Style.Fill.BackgroundColor.SetColor(color);
						}
					}
				}

				// Freeze the top row and setup auto-filter
				worksheet.View.FreezePanes(2, 1);
				worksheet.Cells[1, 1, 1, _reportColumns.Length].AutoFilter = true;

				// Format the worksheet
				worksheet.Row(1).Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
				if (auditedAddins.Any())
				{
					for (int i = 0; i < _reportColumns.Length; i++)
					{
						worksheet.Cells[2, i + 1, row, i + 1].Style.HorizontalAlignment = _reportColumns[i].Align;
					}
				}

				// Resize columns
				worksheet.Cells[1, 1, row, _reportColumns.Length].AutoFitColumns();

				// Make columns a little bit wider to account for the filter "drop-down arrow" button
				for (int i = 0; i < _reportColumns.Length; i++)
				{
					worksheet.Column(i + 1).Width = worksheet.Column(i + 1).Width + 2.14;
				}

				// Exceptions report
				if (exceptionAddins.Any())
				{
					worksheet = package.Workbook.Worksheets.Add("Exceptions");

					worksheet.Cells[1, 1].Value = "Addin";
					worksheet.Cells[1, 2].Value = "Notes";

					row = 1;
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

		private string GenerateMarkdownReport(IEnumerable<AddinMetadata> addins)
		{
			Console.WriteLine("  Generating markdown report");

			var auditedAddins = addins.Where(addin => string.IsNullOrEmpty(addin.AnalysisResult.Notes));
			var exceptionAddins = addins.Where(addin => !string.IsNullOrEmpty(addin.AnalysisResult.Notes));

			var version = string.Empty;
			var assemblyVersion = typeof(AddinDiscoverer).GetTypeInfo().Assembly.GetName().Version;
#if DEBUG
			version = "DEBUG";
#else
			version = $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
#endif

			var markdown = new StringBuilder();

			markdown.AppendLine("# Information");
			markdown.AppendLine();
			markdown.AppendLine($"- This report was generated by Cake.AddinDiscoverer v{version} on {DateTime.UtcNow.ToLongDateString()} at {DateTime.UtcNow.ToLongTimeString()} GMT");
			markdown.AppendLine($"- The desired Cake version is `{_options.RecommendedCakeVersion}`");
			markdown.AppendLine($"- The `Cake Core Version` and `Cake Common Version` columns  show the version referenced by a given addin");
			markdown.AppendLine($"- The `Cake Core IsPrivate` and `Cake Common IsPrivate` columns indicate whether the references are marked as private. In other words, we are looking for references with the `PrivateAssets=All` attribute like in this example: `<PackageReference Include=\"Cake.Common\" Version=\"{_options.RecommendedCakeVersion}\" PrivateAssets=\"All\" />`");
			markdown.AppendLine($"- The `Framework` column shows the .NET framework(s) targeted by a given addin. As of Cake 0.26.0, addins should target netstandard2.0 only (there is no need to multi-target)");
			markdown.AppendLine($"- The `Icon` column indicates if the nuget package for your addin uses the cake-contrib icon.");
			markdown.AppendLine($"- The `YAML` column indicates if there is a `.yml` file describing the addin in this [repo](https://github.com/cake-build/website/tree/develop/addins).");
			markdown.AppendLine();

			markdown.AppendLine("# Statistics");
			markdown.AppendLine();
			markdown.AppendLine($"- The analysis discovered {addins.Count()} addins");
			markdown.AppendLine($"  - {auditedAddins.Count()} were successfully audited (see the 'Addins' section)");
			markdown.AppendLine($"  - {exceptionAddins.Count()} could not be audited (see the 'Exceptions' section)");
			markdown.AppendLine();

			markdown.AppendLine($"- Of the {auditedAddins.Count()} audited addins:");
			markdown.AppendLine($"  - {auditedAddins.Count(addin => addin.AnalysisResult.TargetsExpectedFramework) / (double)auditedAddins.Count():P1} are targeting the desired .NET framework");
			markdown.AppendLine($"  - {auditedAddins.Count(addin => addin.AnalysisResult.UsingCakeContribIcon) / (double)auditedAddins.Count():P1} are using the cake-contrib icon");
			markdown.AppendLine($"  - {auditedAddins.Count(addin => addin.AnalysisResult.HasYamlFileOnWebSite) / (double)auditedAddins.Count():P1} have a YAML file on the cake web site");
			markdown.AppendLine();

			var addinsReferencingCakeCore = auditedAddins.Where(addin => !string.IsNullOrEmpty(addin.AnalysisResult.CakeCoreVersion));
			markdown.AppendLine($"- Of the {addinsReferencingCakeCore.Count()} audited addins that reference Cake.Core:");
			markdown.AppendLine($"  - {addinsReferencingCakeCore.Count(addin => addin.AnalysisResult.CakeCoreIsUpToDate) / (double)addinsReferencingCakeCore.Count():P1} are targeting the desired version of Cake.Core");
			markdown.AppendLine($"  - {addinsReferencingCakeCore.Count(addin => addin.AnalysisResult.CakeCoreIsPrivate) / (double)addinsReferencingCakeCore.Count():P1} have marked the reference to Cake.Core as private");
			markdown.AppendLine();

			var addinsReferencingCakeCommon = auditedAddins.Where(addin => !string.IsNullOrEmpty(addin.AnalysisResult.CakeCommonVersion));
			markdown.AppendLine($"- Of the {addinsReferencingCakeCommon.Count()} audited addins that reference Cake.Common:");
			markdown.AppendLine($"  - {addinsReferencingCakeCommon.Count(addin => addin.AnalysisResult.CakeCommonIsUpToDate) / (double)addinsReferencingCakeCommon.Count():P1} are targeting the desired version of Cake.Common");
			markdown.AppendLine($"  - {addinsReferencingCakeCommon.Count(addin => addin.AnalysisResult.CakeCommonIsPrivate) / (double)addinsReferencingCakeCommon.Count():P1} have marked the reference to Cake.Common as private");
			markdown.AppendLine();

			// Title
			markdown.AppendLine("# Addins");
			markdown.AppendLine();

			// Header row 1
			for (int i = 0; i < _reportColumns.Length; i++)
			{
				markdown.Append($"| {_reportColumns[i].Header} ");
			}

			markdown.AppendLine("|");

			// Header row 2
			for (int i = 0; i < _reportColumns.Length; i++)
			{
				markdown.Append("| ");
				if (_reportColumns[i].Align == ExcelHorizontalAlignment.Center) markdown.Append(":");
				markdown.Append("---");
				if (_reportColumns[i].Align == ExcelHorizontalAlignment.Right || _reportColumns[i].Align == ExcelHorizontalAlignment.Center) markdown.Append(":");
				markdown.Append(" ");
			}

			markdown.AppendLine("|");

			// One row per addin
			foreach (var addin in auditedAddins.OrderBy(p => p.Name))
			{
				for (int i = 0; i < _reportColumns.Length; i++)
				{
					var emoji = string.Empty;
					var color = _reportColumns[i].GetCellColor(addin);
					if (color == Color.LightGreen) emoji = GREEN_EMOJI;
					else if (color == Color.Red) emoji = RED_EMOJI;
					markdown.Append($"| {_reportColumns[i].GetContent(addin)} {emoji}");
				}

				markdown.AppendLine("|");
			}

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

			return markdown.ToString();
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
						Maintainer = cells[1].Trim()
					};

					return metadata;
				})
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

			if (start == -1 || end == -1) return string.Empty;

			return content
				.Substring(start + startMark.Length, end - start - startMark.Length)
				.Trim();
		}

		private IEnumerable<(string Id, string Version, bool IsPrivate)> GetProjectReferencesAsync(AddinMetadata addin, string projectPath)
		{
			var references = new List<(string Id, string Version, bool IsPrivate)>();

			var fileSystem = new FileSystem();
			var projectFile = fileSystem.GetFile(new FilePath(projectPath));
			var parsedProject = projectFile.ParseProjectFile("Release");

			foreach (var reference in parsedProject.References)
			{
				var parts = reference.Include.Split(',', StringSplitOptions.RemoveEmptyEntries);
				var referenceDetails = parts.Skip(1).Select(p => p.Split('=', StringSplitOptions.RemoveEmptyEntries));

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

		private IEnumerable<string> GetProjectFrameworksAsync(AddinMetadata addin, string projectPath)
		{
			var fileSystem = new FileSystem();
			var projectFile = fileSystem.GetFile(new FilePath(projectPath));
			var parsedProject = projectFile.ParseProjectFile("Release");

			return parsedProject.TargetFrameworkVersions;
		}

		private async Task<Uri> GetNormalizedProjectUrl(Uri projectUri)
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

		private async Task CommitMarkdownReportToRepoAsync(string markdownReport)
		{
			Console.WriteLine("  Commiting markdown report to cake-contrib/home repo");

			var repositoryName = "Home";
			var owner = "cake-contrib";

			// 1. Get the SHA of the latest commit of the master branch.
			var headMasterRef = "heads/master";
			var masterReference = await _githubClient.Git.Reference.Get(owner, repositoryName, headMasterRef).ConfigureAwait(false); // Get reference of master branch
			var latestCommit = await _githubClient.Git.Commit.Get(owner, repositoryName, masterReference.Object.Sha).ConfigureAwait(false); // Get the laster commit of this branch
			var tree = new NewTree { BaseTree = latestCommit.Tree.Sha };

			// 2. Create the blob(s) corresponding to your file(s)
			var textBlob = new NewBlob
			{
				Encoding = EncodingType.Utf8,
				Content = markdownReport
			};
			var textBlobRef = await _githubClient.Git.Blob.Create(owner, repositoryName, textBlob).ConfigureAwait(false);

			// 3. Create a new tree with:
			const string FILE_MODE = "100644";
			tree.Tree.Add(new NewTreeItem
			{
				Path = "Audit.md",
				Mode = FILE_MODE,
				Type = TreeType.Blob,
				Sha = textBlobRef.Sha
			});
			var newTree = _githubClient.Git.Tree.Create(owner, repositoryName, tree).Result;

			// 4. Create the commit with the SHAs of the tree and the reference of master branch
			// Create Commit
			var newCommit = new NewCommit("Update addin audit", newTree.Sha, masterReference.Object.Sha);
			var commit = _githubClient.Git.Commit.Create(owner, repositoryName, newCommit).Result;

			// 5. Update the reference of master branch with the SHA of the commit
			// Update HEAD with the commit
			await _githubClient.Git.Reference.Update(owner, repositoryName, headMasterRef, new ReferenceUpdate(commit.Sha)).ConfigureAwait(false);
		}
	}
}
