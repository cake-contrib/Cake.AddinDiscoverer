using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
using Octokit;
using Octokit.Internal;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class ValidateUrlStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => true;

		public string GetDescription(DiscoveryContext context) => "Validate URLs";

		public async Task ExecuteAsync(DiscoveryContext context, TextWriter log, CancellationToken cancellationToken)
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
							// Only overwrite GitHub and Bitbucket URLs and preserve custom URLs such as 'https://cakeissues.net/' for example.
							if (addin.ProjectUrl.IsGithubUrl(false) || addin.ProjectUrl.IsBitbucketUrl())
							{
								addin.ProjectUrl = new Uri(repo.HtmlUrl);
							}

							addin.InferredRepositoryUrl = new Uri(repo.CloneUrl);
						}

						// Derive the repository name and owner
						var ownershipDerived = Misc.DeriveGitHubRepositoryInfo(addin.InferredRepositoryUrl ?? addin.RepositoryUrl ?? addin.ProjectUrl, out string repoOwner, out string repoName);
						if (ownershipDerived)
						{
							addin.RepositoryOwner = repoOwner;
							addin.RepositoryName = repoName;
						}

						// Validate GitHub URL
						if (repo == null && ownershipDerived)
						{
							try
							{
								var repository = await context.GithubClient.Repository.Get(repoOwner, repoName).ConfigureAwait(false);

								// Only overwrite GitHub and Bitbucket URLs and preserve custom URLs such as 'https://cakeissues.net/' for example.
								if (addin.ProjectUrl.IsGithubUrl(false) || addin.ProjectUrl.IsBitbucketUrl())
								{
									addin.ProjectUrl = new Uri(repository.HtmlUrl);
								}

								addin.InferredRepositoryUrl = new Uri(repository.CloneUrl);

								// Derive the repository name and owner with the new repo URL
								if (Misc.DeriveGitHubRepositoryInfo(addin.InferredRepositoryUrl, out repoOwner, out repoName))
								{
									addin.RepositoryOwner = repoOwner;
									addin.RepositoryName = repoName;
								}
							}
							catch (NotFoundException)
							{
								addin.ProjectUrl = null;
							}
#pragma warning disable CS0168 // Variable is declared but never used
							catch (Exception e)
#pragma warning restore CS0168 // Variable is declared but never used
							{
								throw;
							}
						}

						// Validate non-GitHub URL
						if (addin.ProjectUrl != null && !addin.ProjectUrl.IsGithubUrl(false))
						{
							var githubRequest = new Request()
							{
								BaseAddress = new UriBuilder(addin.ProjectUrl.Scheme, addin.ProjectUrl.Host, addin.ProjectUrl.Port).Uri,
								Endpoint = new Uri(addin.ProjectUrl.PathAndQuery, UriKind.Relative),
								Method = HttpMethod.Head,
							};
							githubRequest.Headers.Add("User-Agent", ((Connection)context.GithubClient.Connection).UserAgent);

							var response = await SendRequestWithRetries(githubRequest, context.GithubHttpClient).ConfigureAwait(false);

							if (response.StatusCode == HttpStatusCode.NotFound)
							{
								addin.ProjectUrl = null;
							}
						}

						// Standardize GitHub URLs
						addin.InferredRepositoryUrl = Misc.StandardizeGitHubUri(addin.InferredRepositoryUrl);
						addin.RepositoryUrl = Misc.StandardizeGitHubUri(addin.RepositoryUrl);
						addin.ProjectUrl = Misc.StandardizeGitHubUri(addin.ProjectUrl);

						return addin;
					}, Constants.MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);
		}

		private static async Task<IResponse> SendRequestWithRetries(IRequest request, IHttpClient httpClient)
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
