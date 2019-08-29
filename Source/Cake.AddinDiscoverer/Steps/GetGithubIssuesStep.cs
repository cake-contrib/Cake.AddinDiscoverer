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
		public bool PreConditionIsMet(DiscoveryContext context) => true;

		public string GetDescription(DiscoveryContext context) => "Get previously created Github issues and pull requests";

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

			context.IssuesCreatedByCurrentUser = allIssuesAndPullRequests
				.Where(i => i.PullRequest == null)
				.ToList();

			context.PullRequestsCreatedByCurrentUser = allIssuesAndPullRequests
				.Where(i => i.PullRequest != null)
				.Select(i => new PullRequest(
					i.Id,
					i.NodeId,
					i.PullRequest.Url,
					i.PullRequest.HtmlUrl,
					i.PullRequest.DiffUrl,
					i.PullRequest.PatchUrl,
					i.PullRequest.IssueUrl,
					i.PullRequest.StatusesUrl,
					i.Number,
					ItemState.Open,
					i.Title,
					i.Body,
					i.CreatedAt,
					i.UpdatedAt.GetValueOrDefault(),
					i.ClosedAt,
					i.PullRequest.MergedAt,
					null, // head
					new GitReference(i.NodeId, i.Url, null, null, null, i.User, i.Repository),
					i.User,
					i.Assignee,
					i.Assignees,
					i.PullRequest.Mergeable,
					i.PullRequest.MergeableState?.Value,
					i.PullRequest.MergedBy,
					i.PullRequest.MergeCommitSha,
					i.PullRequest.Comments,
					i.PullRequest.Commits,
					i.PullRequest.Additions,
					i.PullRequest.Deletions,
					i.PullRequest.ChangedFiles,
					i.PullRequest.Milestone,
					i.Locked,
					i.PullRequest.MaintainerCanModify,
					i.PullRequest.RequestedReviewers))
				.ToList();

			context.Addins = context.Addins
				.Select(addin =>
				{
					if (!string.IsNullOrEmpty(addin.RepositoryName) && !string.IsNullOrEmpty(addin.RepositoryOwner))
					{
						// Get the issues and pull requests for this addin
						var issuesAndPullRequestsForThisAddin = allIssuesAndPullRequests
							.Where(i => i.Repository.Name.EqualsIgnoreCase(addin.RepositoryName));

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
