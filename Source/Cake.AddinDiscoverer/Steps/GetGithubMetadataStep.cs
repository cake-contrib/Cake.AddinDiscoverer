using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
using Newtonsoft.Json.Linq;
using Octokit;
using Octokit.Internal;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class GetGithubMetadataStep : IStep
	{
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
								// Total count includes both issues and pull requests.
								var totalCount = await GetRecordsCount(context, "issues", addinsGroup.Key.RepositoryOwner, addinsGroup.Key.RepositoryName).ConfigureAwait(false);
								var pullRequestsCount = await GetRecordsCount(context, "pulls", addinsGroup.Key.RepositoryOwner, addinsGroup.Key.RepositoryName).ConfigureAwait(false);
								var issuesCount = totalCount - pullRequestsCount;

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

		private static async Task<int> GetRecordsCount(DiscoveryContext context, string type, string repositoryOwner, string repositoryName)
		{
			// Send a HTTP request to Github for issues with only one issue per page (notice "per_page=1", this is important).
			// The response will include a header called "Link" containing URLs for the "next" page and also for the "last" page.
			// The link for the "last" page will contain a querystring parameter like: "page=2". This value indicates the total
			// number of records. This works because we requested one record per page.
			var githubRequest = new Request()
			{
				BaseAddress = new Uri("https://api.github.com"),
				Endpoint = new Uri($"/repos/{repositoryOwner}/{repositoryName}/{type}?state=open&per_page=1&page=1", UriKind.Relative),
				Method = HttpMethod.Get,
			};
			var connection = (Connection)context.GithubClient.Connection;
			githubRequest.Headers.Add("Authorization", $"Basic {Convert.ToBase64String(Encoding.ASCII.GetBytes(string.Concat(connection.Credentials.Login, ":", connection.Credentials.Password)))}");
			githubRequest.Headers.Add("User-Agent", connection.UserAgent);

			var githubResponse = await context.GithubHttpClient.Send(githubRequest).ConfigureAwait(false);

			var recordsCount = 0;
			var lastPageUrl = githubResponse.ApiInfo.GetLastPageUrl();
			if (lastPageUrl != null)
			{
				var pageParameter = lastPageUrl.ParseQuerystring().Where(p => p.Key.EqualsIgnoreCase("page"));
				if (pageParameter.Any())
				{
					var lastPageNumber = pageParameter.Single().Value;
					int.TryParse(lastPageNumber, out recordsCount);
				}
			}
			else
			{
				// The link for the "last" page is not present in the response header.
				// Check if there is a record in the content.
				try
				{
					var records = JArray.Parse(githubResponse.Body.ToString());
					recordsCount = records.Count;
				}
				catch
				{
					recordsCount = 0;
				}
			}

			return recordsCount;
		}
	}
}
