using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
using Octokit;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class GetGithubIssuesStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => context.Options.CreateGithubIssue || context.Options.SubmitGithubPullRequest;

		public string GetDescription(DiscoveryContext context) => "Get issues and pull requests from Github";

		public async Task ExecuteAsync(DiscoveryContext context)
		{
			// Get all issues and pull requests created by the current user.
			// This information will be used to avoid creating duplicates.
			var request = new IssueRequest()
			{
				Filter = IssueFilter.Created,
				State = ItemStateFilter.Open,
				SortProperty = IssueSort.Created,
				SortDirection = SortDirection.Descending,
			};

			var allIssuesAndPullRequests = await context.GithubClient.Issue.GetAllForCurrent(request).ConfigureAwait(false);

			context.Addins = context.Addins
				.Select(addin =>
				{
					if (!string.IsNullOrEmpty(addin.RepositoryName) && !string.IsNullOrEmpty(addin.RepositoryOwner))
					{
						// Get the issues and pull requests for this addin
						var issuesAndPullRequestsForThisAddin = allIssuesAndPullRequests
							.Where(i => i.Repository.Owner.Login.EqualsIgnoreCase(addin.RepositoryOwner) && i.Repository.Name.EqualsIgnoreCase(addin.RepositoryName));

						// Get the previously created issue titled: "Recommended changes resulting from automated audit"
						addin.GithubIssueId = issuesAndPullRequestsForThisAddin
							.Where(i => i.PullRequest == null)
							.FirstOrDefault(i => i.Title.EqualsIgnoreCase(Constants.ISSUE_TITLE) || i.Body.StartsWith("We performed an automated audit of your Cake addin", StringComparison.OrdinalIgnoreCase))?
							.Number;

						// Get the previously created pull request titled: "Fix issues identified by automated audit"
						addin.GithubPullRequestId = issuesAndPullRequestsForThisAddin
							.Where(i => i.PullRequest != null)
							.FirstOrDefault(i => i.Title.EqualsIgnoreCase(Constants.PULL_REQUEST_TITLE))?
							.Number;
					}

					return addin;
				})
				.ToArray();
		}
	}
}
