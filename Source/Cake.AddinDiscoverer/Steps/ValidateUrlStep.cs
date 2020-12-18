using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
using Octokit;
using Octokit.Internal;
using System;
using System.Linq;
using System.Net;
using System.Threading;
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

			context.Addins = await context.Addins
				.ForEachAsync(
					async addin =>
					{
						// Some addins were moved to the cake-contrib organization but the URL in their package metadata still
						// points to the original repo. This step corrects the URL to ensure it points to the right repo
						var repo = cakeContribRepositories.FirstOrDefault(r => r.Name.EqualsIgnoreCase(addin.Name));
						if (repo != null)
						{
							addin.ProjectUrl = new Uri(repo.HtmlUrl);
							addin.InferredRepositoryUrl = new Uri(repo.CloneUrl);
						}

						// Standardize GitHub URLs
						addin.InferredRepositoryUrl = Misc.StandardizeGitHubUri(addin.InferredRepositoryUrl);
						addin.RepositoryUrl = Misc.StandardizeGitHubUri(addin.RepositoryUrl);
						addin.ProjectUrl = Misc.StandardizeGitHubUri(addin.ProjectUrl);

						// Derive the repository name and owner
						var ownershipDerived = Misc.DeriveRepoInfo(addin.InferredRepositoryUrl ?? addin.RepositoryUrl ?? addin.ProjectUrl, out string repoOwner, out string repoName);
						addin.RepositoryOwner = repoOwner;
						addin.RepositoryName = repoName;

						// Make sure the project URL is valid
						if (ownershipDerived)
						{
							try
							{
								var repository = await context.GithubClient.Repository.Get(repoOwner, repoName).ConfigureAwait(false);
								addin.InferredRepositoryUrl = new Uri(repository.CloneUrl);

								// Derive the repository name and owner with the new repo URL
								if (Misc.DeriveRepoInfo(addin.InferredRepositoryUrl, out repoOwner, out repoName))
								{
									addin.RepositoryOwner = repoOwner;
									addin.RepositoryName = repoName;
								}
							}
							catch (NotFoundException)
							{
								addin.ProjectUrl = null;
							}
							catch (Exception e)
							{
								throw;
							}
						}

						return addin;
					}, Constants.MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);
		}

		private async Task<IResponse> SendRequestWithRetries(IRequest request, IHttpClient httpClient)
		{
			IResponse response = null;
			const int maxRetry = 3;
			for (int retryCount = 0; retryCount < maxRetry; retryCount++)
			{
				response = await httpClient.Send(request, CancellationToken.None).ConfigureAwait(false);

				if (response.StatusCode == HttpStatusCode.TooManyRequests && retryCount < maxRetry - 1)
				{
					response.Headers.TryGetValue("Retry-After", out string retryAfter);
					await Task.Delay(1000 * int.Parse(retryAfter ?? "60")).ConfigureAwait(false);
				}
				else
				{
					break;
				}
			}

			return response;
		}
	}
}
