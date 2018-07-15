using Octokit;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer
{
	internal static class Extensions
	{
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

		public static bool IsFlagSet<T>(this T value, T flag)
			where T : struct
		{
			CheckIsEnum<T>(true);
			long lValue = Convert.ToInt64(value);
			long lFlag = Convert.ToInt64(flag);
			return (lValue & lFlag) != 0;
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
