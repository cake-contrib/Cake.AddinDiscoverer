using Octokit;
using Octokit.Internal;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Utilities
{
	internal class CachedRepositoryValidator
	{
		private static readonly ConcurrentDictionary<Uri, Task<HttpStatusCode>> _urlValidationCache = new();
		private static readonly ConcurrentDictionary<string, Task<Repository>> _repoValidationCache = new();
		private static readonly ConcurrentDictionary<string, Task<IDictionary<string, Stream>>> _repoArchiveCache = new();

		public string UserAgent { get; init; }

		public IHttpClient HttpClient { get; init; }

		public IGitHubClient GithubClient { get; init; }

		public CachedRepositoryValidator(string userAgent, IHttpClient httpClient, IGitHubClient githubClient)
		{
			UserAgent = userAgent;
			HttpClient = httpClient;
			GithubClient = githubClient;
		}

		public async Task<HttpStatusCode> ValidateUrlAsync(Uri url)
		{
			var statusCode = await _urlValidationCache.GetOrAddAsync(url, async url =>
			{
				var request = new Request()
				{
					BaseAddress = new UriBuilder(url.Scheme, url.Host, url.Port).Uri,
					Endpoint = new Uri(url.PathAndQuery, UriKind.Relative),
					Method = HttpMethod.Head,
				};
				request.Headers.Add("User-Agent", UserAgent);

				var response = await SendRequestWithRetries(request, HttpClient).ConfigureAwait(false);
				return response.StatusCode;
			}).ConfigureAwait(false);

			return statusCode;
		}

		public async Task<Repository> ValidateGithubRepoAsync(string repoOwner, string repoName)
		{
			var cacheKey = $"{repoOwner}/{repoName}";

			var repo = await _repoValidationCache.GetOrAddAsync(cacheKey, async cacheKey =>
			{
				var parts = cacheKey.Split('/');
				var repository = await GithubClient.Repository.Get(parts[0], parts[1]).ConfigureAwait(false);
				return repository;
			}).ConfigureAwait(false);

			return repo;
		}

		public async Task<IDictionary<string, Stream>> GetRepoContentAsync(string repoOwner, string repoName)
		{
			var cacheKey = $"{repoOwner}/{repoName}";

			var repoArchive = await _repoArchiveCache.GetOrAddAsync(cacheKey, async cacheKey =>
			{
				IDictionary<string, Stream> repoContent;
				var parts = cacheKey.Split('/');

				var zipArchive = await GithubClient.Repository.Content.GetArchive(parts[0], parts[1], ArchiveFormat.Zipball).ConfigureAwait(false);
				using (var data = new MemoryStream(zipArchive))
				{
					using var archive = new ZipArchive(data);

					repoContent = archive.Entries
						.ToDictionary(
							item => item.FullName,
							item =>
							{
								var ms = new MemoryStream();

								item.Open().CopyTo(ms);

								ms.Position = 0;
								return (Stream)ms;
							});
				}

				return repoContent;
			}).ConfigureAwait(false);

			return repoArchive;
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
