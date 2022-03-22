using Cake.AddinDiscoverer.Models;
using Octokit;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class GetContributorsStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => true;

		public string GetDescription(DiscoveryContext context) => "Get the list of people who contributed to Cake";

		public async Task ExecuteAsync(DiscoveryContext context, TextWriter log, CancellationToken cancellationToken)
		{
			var apiOptions = new Octokit.ApiOptions() { PageSize = 500 };

			// Get all the public repositories
			var repos = await context.GithubClient.Repository.GetAllForOrg("cake-build", apiOptions).ConfigureAwait(false);
			var publicRepos = repos.Where(repo => !repo.Private).ToArray();

			// Get the contributors for each repository
			var allContributors = new List<RepositoryContributor>(250);
			foreach (var publicRepo in publicRepos)
			{
				var repoContributors = await context.GithubClient.Repository.GetAllContributors(publicRepo.Id, false, apiOptions).ConfigureAwait(false);
				allContributors.AddRange(repoContributors);
			}

			// Remove duplicates and sort alphabetically
			var contributors = allContributors
				.DistinctBy(contributor => contributor.Id)
				.OrderBy(contributor => contributor.Login)
				.Select(contributor => new
				{
					AvatarUrl = contributor.AvatarUrl,
					Name = contributor.Login,
					HtmlUrl = contributor.HtmlUrl,
				})
				.ToArray();

			var yaml = contributors.ToYamlString("\n");

		}
	}
}
