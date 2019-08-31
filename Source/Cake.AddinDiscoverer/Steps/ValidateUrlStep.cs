using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
using System;
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
					var repo = cakeContribRepositories.FirstOrDefault(r => r.Name.EqualsIgnoreCase(addin.Name));
					if (repo != null)
					{
						addin.ProjectUrl = new Uri(repo.HtmlUrl);
					}

					// Force HTTPS for Github URLs
					if (addin.ProjectUrl.MustUseHttps()) addin.ProjectUrl = addin.ProjectUrl.ForceHttps();
					if (addin.RepositoryUrl.MustUseHttps()) addin.RepositoryUrl = addin.RepositoryUrl.ForceHttps();

					// Derive the repository name and owner
					var (repoOwner, repoName) = Misc.DeriveRepoInfo(addin.RepositoryUrl ?? addin.ProjectUrl);
					addin.RepositoryOwner = repoOwner;
					addin.RepositoryName = repoName;

					return addin;
				})
				.ToArray();
		}
	}
}
