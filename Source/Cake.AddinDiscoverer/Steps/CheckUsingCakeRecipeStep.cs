using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
using Octokit;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class CheckUsingCakeRecipeStep : IStep
	{
		private static string[] WELL_KNOWN_CAKE_FILES = new[]
		{
			"setup.cake",
			"build.cake",
			"recipe.cake"
		};

		public bool PreConditionIsMet(DiscoveryContext context) => !context.Options.ExcludeSlowSteps && (context.Options.ExcelReportToFile || context.Options.ExcelReportToRepo);

		public string GetDescription(DiscoveryContext context)
		{
			if (string.IsNullOrEmpty(context.Options.AddinName)) return "Check if addins are using Cake.Recipe";
			else return $"Check if {context.Options.AddinName} is using Cake.Recipe";
		}

		public async Task ExecuteAsync(DiscoveryContext context)
		{
			context.Addins = await context.Addins
				.ForEachAsync(
					async addin =>
					{
						if (!string.IsNullOrEmpty(addin.RepositoryName) && !string.IsNullOrEmpty(addin.RepositoryOwner))
						{
							try
							{
								// Get all files from the repo
								var filesGroupedByExtention = await Misc.GetFilePathsFromRepoAsync(context, addin).ConfigureAwait(false);

								// Get the cake files
								filesGroupedByExtention.TryGetValue(".cake", out string[] filePaths);
								if (filePaths != null && filePaths.Any())
								{
									// The purpose of ordering is to favor well known cake files if they are present in the repository
									// and therefore limit the number of calls to github's API while at the same time account for the
									// fact that addin authors may not necessarily follow the usual naming convention
									foreach (var filePath in filePaths.OrderBy(path => WELL_KNOWN_CAKE_FILES.Contains(Path.GetFileName(path)) ? 0 : 1))
									{
										// Get the content of the cake file
										var cakeFileContent = await Misc.GetFileContentFromRepoAsync(context, addin, filePath).ConfigureAwait(false);

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
												addin.AnalysisResult.CakeRecipePrerelease = cakeRecipeReference.Prerelease;
											}
										}

										if (addin.AnalysisResult.CakeRecipeIsUsed) break;
									}
								}
							}
							catch (ApiException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
							{
								// I know of at least one case where the URL in the NuGet metadata points to a repo that has been deleted.
								// Therefore it's safe to ignore this error.
							}
							finally
							{
								// This is to ensure we don't issue requests too quickly and therefore trigger Github's abuse detection
								await Task.Delay(2500).ConfigureAwait(false);
							}
						}

						return addin;
					}, Constants.MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);
		}
	}
}
