using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer
{
	internal static class Extensions
	{
		public static string WithRightPadding(this string content, int desiredLength)
		{
			var count = Math.Max(0, desiredLength - content.Length);
			return content + new string(' ', count);
		}

		public static async Task<TResult[]> ForEachAsync<T, TResult>(this IEnumerable<T> items, Func<T, Task<TResult>> action, int maxThreads)
		{
			var allTasks = new List<Task<TResult>>();
			var throttler = new SemaphoreSlim(initialCount: maxThreads);
			foreach (var item in items)
			{
				await throttler.WaitAsync();
				allTasks.Add(
					Task.Run(async () =>
					{
						var result = await action(item).ConfigureAwait(false);
						throttler.Release();
						return result;
					}));
			}

			var results = await Task.WhenAll(allTasks).ConfigureAwait(false);
			return results;
		}

		public static async Task ForEachAsync<T>(this IEnumerable<T> items, Func<T, Task> action, int maxThreads)
		{
			var allTasks = new List<Task>();
			var throttler = new SemaphoreSlim(initialCount: maxThreads);
			foreach (var item in items)
			{
				await throttler.WaitAsync();
				allTasks.Add(
					Task.Run(async () =>
					{
						await action(item).ConfigureAwait(false);
						throttler.Release();
					}));
			}

			await Task.WhenAll(allTasks).ConfigureAwait(false);
		}
	}
}
