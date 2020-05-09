using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
using Octokit;
using Octokit.Internal;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Connection = Octokit.Connection;

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
						}

						// Force HTTPS for Github URLs
						if (addin.ProjectUrl.MustUseHttps()) addin.ProjectUrl = addin.ProjectUrl.ForceHttps();
						if (addin.RepositoryUrl.MustUseHttps()) addin.RepositoryUrl = addin.RepositoryUrl.ForceHttps();

						// Make sure the project URL is valid
						if (addin.ProjectUrl != null && !context.Options.ExcludeSlowSteps)
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

						// Derive the repository name and owner
						var (repoOwner, repoName) = Misc.DeriveRepoInfo(addin.RepositoryUrl ?? addin.ProjectUrl);
						addin.RepositoryOwner = repoOwner;
						addin.RepositoryName = repoName;

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
