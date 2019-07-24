using Cake.AddinDiscoverer.Utilities;
using System;
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
						if (!addin.GithubPullRequestId.HasValue && !string.IsNullOrEmpty(addin.RepositoryOwner) && !string.IsNullOrEmpty(addin.RepositoryName))
						{
							try
							{
								var pullRequest = await Misc.FindGithubPullRequestAsync(context, addin.RepositoryOwner, addin.RepositoryName, context.Options.GithubUsername, Constants.PULL_REQUEST_TITLE).ConfigureAwait(false);
								addin.GithubPullRequestId = pullRequest?.Number;
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
