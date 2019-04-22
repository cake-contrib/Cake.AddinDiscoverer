using Cake.AddinDiscoverer.Utilities;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class SubmitGithubPullRequest : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => context.Options.SubmitGithubPullRequest;

		public string GetDescription(DiscoveryContext context) => "Submit Github pull requests";

		public async Task ExecuteAsync(DiscoveryContext context)
		{
			var recommendedCakeVersion = Constants.CAKE_VERSIONS
				.OrderByDescending(cakeVersion => cakeVersion.Version)
				.First();

			context.Addins = await context.Addins
				.ForEachAsync(
					async addin =>
					{
						if (addin.Type != AddinType.Recipe &&
						addin.GithubIssueId.HasValue &&
						!string.IsNullOrEmpty(addin.GithubRepoName) &&
						!string.IsNullOrEmpty(addin.GithubRepoOwner))
						{
							var directoryContent = await context.GithubClient.Repository.Content.GetAllContents(addin.GithubRepoOwner, addin.GithubRepoName).ConfigureAwait(false);
							var nuspecFiles = directoryContent
								.Where(file => file.Name.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
								.ToArray();
						}

						return addin;
					}, Constants.MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);
			{
			}
		}
	}
}
