using Cake.AddinDiscoverer.Models;
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
		private static readonly string[] _wellKnownCakeFiles = new[]
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
			var cakeRecipeAddin = context.Addins
				.Where(a => a.Type == AddinType.Recipe)
				.FirstOrDefault(a => a.Name.EqualsIgnoreCase("Cake.Recipe"));

			var latestCakeRecipeVersion = SemVersion.Parse(cakeRecipeAddin == null ? "0.0.0" : cakeRecipeAddin.NuGetPackageVersion);

			context.Addins = await context.Addins
				.ForEachAsync(
					async addin =>
					{
						if (!string.IsNullOrEmpty(addin.RepositoryName) && !string.IsNullOrEmpty(addin.RepositoryOwner))
						{
							try
							{
								// Get the cake files
								var repoItems = addin.RepoContent
									.Where(item => Path.GetExtension(item.Key) == ".cake")
									.ToArray();

								foreach (var repoItem in repoItems)
								{
									// Get the content of the cake file
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
											addin.AnalysisResult.CakeRecipeVersion = string.IsNullOrEmpty(cakeRecipeReference.ReferencedVersion) ? null : SemVersion.Parse(cakeRecipeReference.ReferencedVersion);
											addin.AnalysisResult.CakeRecipeIsPrerelease = cakeRecipeReference.Prerelease;
											addin.AnalysisResult.CakeRecipeIsLatest = string.IsNullOrEmpty(cakeRecipeReference.ReferencedVersion) || cakeRecipeReference.ReferencedVersion == latestCakeRecipeVersion;
										}
									}

									if (addin.AnalysisResult.CakeRecipeIsUsed) break;
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
