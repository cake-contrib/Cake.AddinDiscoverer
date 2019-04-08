﻿using Octokit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer
{
	internal static class Extensions
	{
		public static void AppendUnixLine(this StringBuilder sb, string value)
		{
			sb.Append($"{value}\n");
		}

		public static async Task<Commit> ModifyFilesAsync(this IGitHubClient githubClient, Repository repo, Commit parentCommit, IEnumerable<string> filesToDelete, IEnumerable<(EncodingType Encoding, string Path, string Content)> filesToUpsert, string commitMessage)
		{
			if (filesToDelete == null && filesToUpsert == null) throw new ArgumentNullException("You must specify at least one file to delete or one file to add/modify");

			// Build the tree with the existing items
			var nt = new NewTree();
			var currentTree = await githubClient.Git.Tree.GetRecursive(repo.Owner.Login, repo.Name, parentCommit.Tree.Sha).ConfigureAwait(false);
			currentTree.Tree
				.Where(x => x.Type != TreeType.Tree)
				.Select(x => new NewTreeItem
				{
					Path = x.Path,
					Mode = x.Mode,
					Type = x.Type.Value,
					Sha = x.Sha
				})
				.ToList()
				.ForEach(x => nt.Tree.Add(x));

			// Remove items from the tree
			if (filesToDelete != null)
			{
				foreach (var filePath in filesToDelete)
				{
					nt.Tree.Remove(nt.Tree.Where(x => x.Path.Equals(filePath)).First());
				}
			}

			// Add or update items in the tree
			if (filesToUpsert != null)
			{
				foreach (var file in filesToUpsert)
				{
					var existingTreeItem = nt.Tree.Where(x => x.Path.Equals(file.Path)).FirstOrDefault();
					if (existingTreeItem != null) nt.Tree.Remove(existingTreeItem);

					var fileBlob = new NewBlob
					{
						Encoding = file.Encoding,
						Content = file.Content
					};
					var fileBlobRef = await githubClient.Git.Blob.Create(repo.Owner.Login, repo.Name, fileBlob).ConfigureAwait(false);
					nt.Tree.Add(new NewTreeItem
					{
						Path = file.Path,
						Mode = AddinDiscoverer.FILE_MODE,
						Type = TreeType.Blob,
						Sha = fileBlobRef.Sha
					});
				}
			}

			// Commit changes
			var newTree = await githubClient.Git.Tree.Create(repo.Owner.Login, repo.Name, nt);
			var newCommit = new NewCommit(commitMessage, newTree.Sha, parentCommit.Sha);
			var latestCommit = await githubClient.Git.Commit.Create(repo.Owner.Login, repo.Name, newCommit);

			return latestCommit;
		}

		public static async Task<TResult[]> ForEachAsync<T, TResult>(this IEnumerable<T> items, Func<T, Task<TResult>> action, int maxDegreeOfParalellism)
		{
			var allTasks = new List<Task<TResult>>();
			var throttler = new SemaphoreSlim(initialCount: maxDegreeOfParalellism);
			foreach (var item in items)
			{
				await throttler.WaitAsync();
				allTasks.Add(
					Task.Run(async () =>
					{
						try
						{
							return await action(item).ConfigureAwait(false);
						}
						finally
						{
							throttler.Release();
						}
					}));
			}

			var results = await Task.WhenAll(allTasks).ConfigureAwait(false);
			return results;
		}

		public static async Task ForEachAsync<T>(this IEnumerable<T> items, Func<T, Task> action, int maxDegreeOfParalellism)
		{
			var allTasks = new List<Task>();
			var throttler = new SemaphoreSlim(initialCount: maxDegreeOfParalellism);
			foreach (var item in items)
			{
				await throttler.WaitAsync();
				allTasks.Add(
					Task.Run(async () =>
					{
						try
						{
							await action(item).ConfigureAwait(false);
						}
						finally
						{
							throttler.Release();
						}
					}));
			}

			await Task.WhenAll(allTasks).ConfigureAwait(false);
		}

		public static async Task<Repository> RefreshFork(this IGitHubClient githubClient, string forkOwner, string forkName)
		{
			var fork = await githubClient.Repository.Get(forkOwner, forkName).ConfigureAwait(false);
			var upstream = fork.Parent;

			var compareResult = await githubClient.Repository.Commit.Compare(upstream.Owner.Login, upstream.Name, upstream.DefaultBranch, $"{fork.Owner.Login}:{fork.DefaultBranch}").ConfigureAwait(false);
			if (compareResult.BehindBy > 0)
			{
				var upstreamBranchReference = await githubClient.Git.Reference.Get(upstream.Owner.Login, upstream.Name, $"heads/{upstream.DefaultBranch}").ConfigureAwait(false);
				await githubClient.Git.Reference.Update(fork.Owner.Login, fork.Name, $"heads/{fork.DefaultBranch}", new ReferenceUpdate(upstreamBranchReference.Object.Sha)).ConfigureAwait(false);
			}

			return fork;
		}

		public static string TrimStart(this string source, string value, StringComparison comparisonType)
		{
			if (source == null)
			{
				throw new ArgumentNullException(nameof(source));
			}

			int valueLength = value.Length;
			int startIndex = 0;
			while (source.IndexOf(value, startIndex, comparisonType) == startIndex)
			{
				startIndex += valueLength;
			}

			return source.Substring(startIndex);
		}

		public static string TrimEnd(this string source, string value, StringComparison comparisonType)
		{
			if (source == null)
			{
				throw new ArgumentNullException(nameof(source));
			}

			int sourceLength = source.Length;
			int valueLength = value.Length;
			int count = sourceLength;
			while (source.LastIndexOf(value, count, comparisonType) == count - valueLength)
			{
				count -= valueLength;
			}

			return source.Substring(0, count);
		}

		public static bool IsFlagSet<T>(this T value, T flag)
			where T : struct
		{
			CheckIsEnum<T>(true);
			long lValue = Convert.ToInt64(value);
			long lFlag = Convert.ToInt64(flag);
			return (lValue & lFlag) != 0;
		}

		/// <summary>
		/// Checks if a string matches another which may include the following wild cards:
		/// ? - any character(one and only one)
		/// * - any characters(zero or more)
		/// </summary>
		/// <param name="source">The string to search for a match</param>
		/// <param name="pattern">The patern to match</param>
		/// <returns>true if a match was found, false otherwise</returns>
		public static bool IsMatch(this string source, string pattern)
		{
			return Regex.IsMatch(source, "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$");
		}

		public static Uri ForceHttps(this Uri originalUri)
		{
			if (originalUri == null) return null;

			return new UriBuilder(originalUri)
			{
				Scheme = Uri.UriSchemeHttps,
				Port = originalUri.IsDefaultPort ? -1 : originalUri.Port // -1 => default port for scheme
			}.Uri;
		}

		private static void CheckIsEnum<T>(bool withFlags)
		{
			if (!typeof(T).IsEnum)
				throw new ArgumentException(string.Format("Type '{0}' is not an enum", typeof(T).FullName));
			if (withFlags && !Attribute.IsDefined(typeof(T), typeof(FlagsAttribute)))
				throw new ArgumentException(string.Format("Type '{0}' doesn't have the 'Flags' attribute", typeof(T).FullName));
		}
	}
}
