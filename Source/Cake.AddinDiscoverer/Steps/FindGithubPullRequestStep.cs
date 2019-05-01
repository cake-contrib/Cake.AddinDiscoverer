using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
using Octokit;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class FindGithubPullRequestStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => context.Options.SubmitGithubPullRequest;

		public string GetDescription(DiscoveryContext context) => "Check if a Github Pull request has been created for addins that do not meet best pratices";

		public async Task ExecuteAsync(DiscoveryContext context)
		{
			context.Addins = await context.Addins
				.ForEachAsync(
					async addin =>
					{
						if (!addin.GithubPullRequestId.HasValue && addin.GithubRepoUrl != null)
						{
							var request = new PullRequestRequest()
							{
								State = ItemStateFilter.Open,
								SortProperty = PullRequestSort.Created,
								SortDirection = SortDirection.Descending
							};

							try
							{
								var pullRequests = await context.GithubClient.PullRequest.GetAllForRepository(addin.GithubRepoOwner, addin.GithubRepoName, request).ConfigureAwait(false);
								var pullRequest = pullRequests.FirstOrDefault(pr => pr.Title.EqualsIgnoreCase(Constants.PULL_REQUEST_TITLE) && pr.User.Login.EqualsIgnoreCase(context.Options.GithubUsername));

								if (pullRequest != null)
								{
									addin.GithubPullRequestId = pullRequest.Number;
								}
							}
							catch (Exception e)
							{
								addin.AnalysisResult.Notes += $"FindGithubPullRequest: {e.GetBaseException().Message}{Environment.NewLine}";
							}
						}

						return addin;
					}, Constants.MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);
		}
	}
}
