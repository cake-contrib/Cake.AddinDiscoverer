using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class ValidateUrlStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => true;

		public string GetDescription(DiscoveryContext context) => "Validate Github repo URLs";

		public async Task ExecuteAsync(DiscoveryContext context)
		{
			var cakeContribRepositories = await context.GithubClient.Repository.GetAllForUser(Constants.CAKE_CONTRIB_REPO_OWNER).ConfigureAwait(false);

			context.Addins = context.Addins
				.Select(addin =>
				{
					// Some addins were moved to the cake-contrib organization but the URL in their package metadata still
					// points to the original repo. This step corrects the URL to ensure it points to the right repo
					addin.ProjectUrl = AdjustUrlIfProjectTransferedToCakeContrib(addin.ProjectUrl, addin.Name, addin.RepositoryOwner, cakeContribRepositories);

					// Force HTTPS for Github projects
					if (addin.ProjectUrl?.Host.Contains("github.com", StringComparison.OrdinalIgnoreCase) ?? false)
					{
						addin.ProjectUrl = addin.ProjectUrl.ForceHttps();
					}

					// Force HTTPS for Github repositories
					if (addin.RepositoryUrl?.Host.Contains("github.com", StringComparison.OrdinalIgnoreCase) ?? false)
					{
						addin.RepositoryUrl = addin.RepositoryUrl.ForceHttps();
					}

					// Derive the repository name and owner
					var (repoOwner, repoName) = DeriveRepoInfo(addin.RepositoryUrl, addin.ProjectUrl);
					addin.RepositoryOwner = repoOwner;
					addin.RepositoryName = repoName;

					return addin;
				})
				.ToArray();
		}

		private static Uri AdjustUrlIfProjectTransferedToCakeContrib(Uri uri, string addinName, string repositoryOwner, IReadOnlyList<Octokit.Repository> cakeContribRepositories)
		{
			if (uri == null ||
				!uri.Host.Contains("github.com", StringComparison.OrdinalIgnoreCase) ||
				repositoryOwner != Constants.CAKE_CONTRIB_REPO_OWNER)
			{
				var repo = cakeContribRepositories.FirstOrDefault(r => r.Name.EqualsIgnoreCase(addinName));
				if (repo != null) return new Uri(repo.HtmlUrl);
			}

			return uri;
		}

		private static (string Owner, string Name) DeriveRepoInfo(Uri repositoryUrl, Uri projectUrl)
		{
			var owner = string.Empty;
			var name = string.Empty;

			var url = repositoryUrl ?? projectUrl;
			if (url != null)
			{
				var parts = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length >= 2)
				{
					owner = parts[0];
					name = parts[1].TrimEnd(".git", StringComparison.OrdinalIgnoreCase);
				}
			}

			return (owner, name);
		}
	}
}
