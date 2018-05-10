using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using YamlDotNet.RepresentationModel;

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

		public static string GetChildNodeValue(this YamlMappingNode yamlNode, string childNodeName)
		{
			return yamlNode.Children.Where(child => ((YamlScalarNode)child.Key).Value == childNodeName).Select(child => ((YamlScalarNode)child.Value).Value).FirstOrDefault();
		}
	}
}
