using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
using GraphQL.Client.Http;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class GetGithubMetadataStep : IStep
	{
		private const string COUNT_OPEN_ISSUES_AND_PULLREQUESTS_GRAPHQL_QUERY = @"
        query CountOpenIssuesAndPullRequests($repoName: String!, $repoOwner: String!)
		{
		  repository(owner: $repoOwner, name: $repoName) {
		    issues(states: OPEN) {
		      totalCount
		    }
		    pullRequests(states: OPEN) {
		      totalCount
		    }
		  }
		}";

		public static async Task<(int IssuesCount, int PullRquestsCount)> GetOpenRecordsCount(DiscoveryContext context, string repositoryOwner, string repositoryName)
		{
			var request = new GraphQLHttpRequest
			{
				Query = COUNT_OPEN_ISSUES_AND_PULLREQUESTS_GRAPHQL_QUERY
					.Replace("\r\n", string.Empty, StringComparison.OrdinalIgnoreCase)
					.Replace("\t", string.Empty, StringComparison.OrdinalIgnoreCase),
				Variables = new
				{
					repoName = repositoryName,
					repoOwner = repositoryOwner,
				},
			};

			var graphQLResponse = await context.GraphQLClient.SendQueryAsync<dynamic>(request).ConfigureAwait(false);

			// Check if the response has errors
			if (graphQLResponse.Errors != null && graphQLResponse.Errors.Length > 0)
			{
				throw new Exception($"GraphQL query failed with errors: {string.Join(", ", graphQLResponse.Errors.Select(e => e.Message))}");
			}

			// Check if data is null (can happen even without explicit errors)
			if (graphQLResponse.Data == null)
			{
				throw new Exception("GraphQL response contains no data");
			}

			// Now safely access the data
			var repoNode = ((JsonElement)graphQLResponse.Data).GetProperty("repository");
			var issuesCount = repoNode.GetProperty("issues").GetProperty("totalCount").GetInt32();
			var pullRequestsCount = repoNode.GetProperty("pullRequests").GetProperty("totalCount").GetInt32();

			return (issuesCount, pullRequestsCount);
		}

		public bool PreConditionIsMet(DiscoveryContext context) => !context.Options.ExcludeSlowSteps && context.Options.GenerateExcelReport;

		public string GetDescription(DiscoveryContext context) => "Get stats from Github (number of open issues, etc.)";

		public async Task ExecuteAsync(DiscoveryContext context, TextWriter log, CancellationToken cancellationToken)
		{
			var addinsGroupedByRepoInfo = context.Addins
				.GroupBy(addin => (addin.RepositoryName, addin.RepositoryOwner))
				.ToArray();

			await addinsGroupedByRepoInfo
				.ForEachAsync(
					async addinsGroup =>
					{
						if (!string.IsNullOrEmpty(addinsGroup.Key.RepositoryName) && !string.IsNullOrEmpty(addinsGroup.Key.RepositoryOwner))
						{
							foreach (AddinMetadata addin in addinsGroup)
							{
								if (!string.IsNullOrEmpty(addin.AnalysisResult.Notes))
								{
									// Get rid of previous notes regarding Github metadata
									// These notes were added until July 25 2026.
									var oldLines = Regex.Split(addin.AnalysisResult.Notes, "\r\n|\r|\n");
									var newLines = oldLines.Where(line => !line.StartsWithIgnoreCase("GetGithubMetadata:")).ToArray();
									addin.AnalysisResult.Notes = string.Join(Environment.NewLine, newLines);
								}

								// Reset the counts to null before attempting to get the actual counts
								addin.AnalysisResult.OpenIssuesCount = null;
								addin.AnalysisResult.OpenPullRequestsCount = null;
							}

							try
							{
								// Get the number of open issues and pull requests
								(int issuesCount, int pullRequestsCount) = await GetOpenRecordsCount(context, addinsGroup.Key.RepositoryOwner, addinsGroup.Key.RepositoryName).ConfigureAwait(false);

								// Update all the addins for this repo
								foreach (AddinMetadata addin in addinsGroup)
								{
									addin.AnalysisResult.OpenIssuesCount = issuesCount;
									addin.AnalysisResult.OpenPullRequestsCount = pullRequestsCount;
								}
							}
							catch
							{
								// It's safe to ignore errors here, as some repos may not allow issues or may have been deleted.
								// Ideally, we should log these errors for further investigation.
							}
							finally
							{
								// This is to ensure we don't issue requests too quickly and therefore trigger Github's abuse detection
								await Misc.RandomGithubDelayAsync().ConfigureAwait(false);
							}
						}
					},
					Constants.MAX_GITHUB_CONCURENCY)
				.ConfigureAwait(false);
		}
	}
}
