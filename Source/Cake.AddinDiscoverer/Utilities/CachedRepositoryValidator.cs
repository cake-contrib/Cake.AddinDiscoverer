using Cake.AddinDiscoverer.Models;
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
		private static readonly ConcurrentDictionary<string, Task<IDictionary<string, Stream>>> _repoContentCache = new();
		private static readonly ConcurrentDictionary<string, Task<IReadOnlyList<RepositoryTag>>> _repoTagsCache = new();

		private readonly DiscoveryContext _context;
		private readonly string _userAgent;

		public CachedRepositoryValidator(DiscoveryContext context)
		{
			_context = context;
			_userAgent = ((Connection)_context.GithubClient.Connection).UserAgent;
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

				request.Headers.Add("User-Agent", _userAgent);

				var response = await SendRequestWithRetries(request, _context.GithubHttpClient).ConfigureAwait(false);
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
				var cacheKeyRepoOwner = parts[0];
				var cacheKeyRepoName = parts[1];
				var repository = await _context.GithubClient.Repository.Get(parts[0], parts[1]).ConfigureAwait(false);
				return repository;
			}).ConfigureAwait(false);

			return repo;
		}

		public async Task<IDictionary<string, Stream>> GetRepoContentAsync(string repoOwner, string repoName, string tag = null)
		{
			if (!string.IsNullOrEmpty(tag))
			{
				var repoTags = await GetRepoTagsAsync(repoOwner, repoName).ConfigureAwait(false);
				var repoTag = repoTags.SingleOrDefault(t => t.Name == tag);

				if (repoTag != null && !string.IsNullOrEmpty(repoTag.ZipballUrl))
				{
					var tagCacheKey = $"{repoOwner}/{repoName}/{repoTag.Name}";

					var repocontent = await _repoContentCache.GetOrAddAsync(tagCacheKey, async cacheKey =>
					{
						var parts = cacheKey.Split('/');
						var cacheKeyRepoOwner = parts[0];
						var cacheKeyRepoName = parts[1];
						var keyTag = parts.Length == 3 ? parts[2] : null;

						var zipArchiveFileName = Path.Combine(_context.ZipArchivesFolder, $"{cacheKeyRepoName}.{keyTag}.bin");

						if (File.Exists(zipArchiveFileName))
						{
							var zipArchiveContent = await File.ReadAllBytesAsync(zipArchiveFileName).ConfigureAwait(false);
							return UnzipArchive(zipArchiveContent);
						}
						else
						{
							var zipArchiveContent = await _context.HttpClient.GetByteArrayAsync(repoTag.ZipballUrl).ConfigureAwait(false);
							await File.WriteAllBytesAsync(zipArchiveFileName, zipArchiveContent).ConfigureAwait(false);
							return UnzipArchive(zipArchiveContent);
						}
					}).ConfigureAwait(false);

					return repocontent;
				}
			}

			var cacheKey = $"{repoOwner}/{repoName}";

			var repoContent = await _repoContentCache.GetOrAddAsync(cacheKey, async cacheKey =>
			{
				var parts = cacheKey.Split('/');
				var cacheKeyRepoOwner = parts[0];
				var cacheKeyRepoName = parts[1];
				var keyTag = parts.Length == 3 ? parts[2] : null;

				var zipArchive = await _context.GithubClient.Repository.Content.GetArchive(cacheKeyRepoOwner, cacheKeyRepoName, ArchiveFormat.Zipball).ConfigureAwait(false);
				return UnzipArchive(zipArchive);
			}).ConfigureAwait(false);

			return repoContent;
		}

		public async Task<IReadOnlyList<RepositoryTag>> GetRepoTagsAsync(string repoOwner, string repoName)
		{
			var cacheKey = $"{repoOwner}/{repoName}";

			var repoTags = await _repoTagsCache.GetOrAddAsync(cacheKey, async cacheKey =>
			{
				var parts = cacheKey.Split('/');
				var cacheKeyRepoOwner = parts[0];
				var cacheKeyRepoName = parts[1];

				return await _context.GithubClient.Repository.GetAllTags(cacheKeyRepoOwner, cacheKeyRepoName).ConfigureAwait(false);
			}).ConfigureAwait(false);

			return repoTags;
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

		private static IDictionary<string, Stream> UnzipArchive(byte[] zipArchive)
		{
			using var data = new MemoryStream(zipArchive);
			using var archive = new ZipArchive(data);

			return archive.Entries
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
	}
}
