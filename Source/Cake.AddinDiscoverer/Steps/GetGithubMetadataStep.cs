using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.SystemTextJson;
using Octokit;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
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
							catch (ApiException e) when (e.ApiError.Message.EqualsIgnoreCase("Issues are disabled for this repo"))
							{
								// There's a NuGet package with a project URL that points to a fork which doesn't allow issues.
								// Therefore it's safe to ignore this error.
							}
							catch (ApiException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
							{
								// I know of at least one case where the URL in the NuGet metadata points to a repo that has been deleted.
								// Therefore it's safe to ignore this error.
							}
							catch (Exception e)
							{
								foreach (AddinMetadata addin in addinsGroup)
								{
									addin.AnalysisResult.Notes += $"GetGithubMetadata: {e.GetBaseException().Message}{Environment.NewLine}";
								}
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

		public static async Task<(int IssuesCount, int PullRquestsCount)> GetOpenRecordsCount(DiscoveryContext context, string repositoryOwner, string repositoryName)
		{
			var connection = (Octokit.Connection)context.GithubClient.Connection;
			var client = new GraphQLHttpClient(new GraphQLHttpClientOptions { EndPoint = new Uri("https://api.github.com/graphql") }, new SystemTextJsonSerializer());
			client.HttpClient.DefaultRequestHeaders.Add("Authorization", $"Basic {Convert.ToBase64String(Encoding.ASCII.GetBytes(string.Concat(connection.Credentials.Login, ":", connection.Credentials.Password)))}");

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

			var graphQLResponse = await client.SendQueryAsync<dynamic>(request).ConfigureAwait(false);

			var repoNode = ((JsonElement)graphQLResponse.Data).GetProperty("repository");
			var issuesCount = repoNode.GetProperty("issues").GetProperty("totalCount").GetInt32();
			var pullRequestsCount = repoNode.GetProperty("pullRequests").GetProperty("totalCount").GetInt32();

			return (issuesCount, pullRequestsCount);
		}
	}
}
