using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
using Octokit;
using System;
using System.Collections.Generic;
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
			var searchRequest = new SearchIssuesRequest()
			{
				Author = context.Options.GithubUsername,
				State = ItemState.Open,
				SortField = IssueSearchSort.Created,
				Order = SortDirection.Descending,
				Page = 1, // Paging is 1-based
				PerPage = 100 // Github's search allows a maximum of 100 records per page
			};

			var allIssuesAndPullRequests = new List<Issue>();
			var moreRecords = true;
			do
			{
				var searchResult = await context.GithubClient.Search.SearchIssues(searchRequest).ConfigureAwait(false);
				allIssuesAndPullRequests.AddRange(searchResult.Items);
				searchRequest.Page++;

				// Check if there are more records to be fetched
				moreRecords = allIssuesAndPullRequests.Count != searchResult.TotalCount;

				// This is a failsafe to avoid looping indefinitely
				if (moreRecords && searchResult.Items.Count == 0)
				{
					throw new Exception($"{searchResult.TotalCount} match the search criteria but we were only able to retrieve {allIssuesAndPullRequests.Count}");
				}
			}
			while (moreRecords);

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
