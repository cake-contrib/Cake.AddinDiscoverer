using Cake.AddinDiscoverer.Utilities;
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
					if (addin.GithubRepoUrl == null ||
						addin.GithubRepoUrl.Host != "github.com" ||
						addin.GithubRepoOwner != Constants.CAKE_CONTRIB_REPO_OWNER)
					{
						try
						{
							var repo = cakeContribRepositories.FirstOrDefault(r => r.Name == addin.Name);
							if (repo != null)
							{
								addin.GithubRepoUrl = new Uri(repo.HtmlUrl).ForceHttps();
							}
						}
						catch (Exception e)
						{
							addin.AnalysisResult.Notes += $"ValidateProjectUrlAsync: {e.GetBaseException().Message}{Environment.NewLine}";
						}
					}
					return addin;
				})
				.ToArray();
		}
	}
}
