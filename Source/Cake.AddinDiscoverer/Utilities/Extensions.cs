using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
using Octokit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using YamlDotNet.RepresentationModel;

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
						Mode = Constants.FILE_MODE,
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
			using var throttler = new SemaphoreSlim(initialCount: maxDegreeOfParalellism);
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
			using var throttler = new SemaphoreSlim(initialCount: maxDegreeOfParalellism);
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

		public static async Task<Repository> CreateOrRefreshFork(this IGitHubClient githubClient, string repoOwner, string repoName)
		{
			var fork = await githubClient.Repository.Forks.Create(repoOwner, repoName, new NewRepositoryFork()).ConfigureAwait(false);
			var upstream = fork.Parent;

			var compareResult = await githubClient.Repository.Commit.Compare(upstream.Owner.Login, upstream.Name, upstream.DefaultBranch, $"{fork.Owner.Login}:{fork.DefaultBranch}").ConfigureAwait(false);
			if (compareResult.BehindBy > 0)
			{
				var upstreamBranchReference = await githubClient.Git.Reference.Get(upstream.Owner.Login, upstream.Name, $"heads/{upstream.DefaultBranch}").ConfigureAwait(false);
				await githubClient.Git.Reference.Update(fork.Owner.Login, fork.Name, $"heads/{fork.DefaultBranch}", new ReferenceUpdate(upstreamBranchReference.Object.Sha)).ConfigureAwait(false);
			}

			return fork;
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
		/// ? - any character(one and only one).
		/// * - any characters(zero or more).
		/// </summary>
		/// <param name="source">The string to search for a match.</param>
		/// <param name="pattern">The pattern to match.</param>
		/// <returns>true if a match was found, false otherwise.</returns>
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

		public static bool MustUseHttps(this Uri uri)
		{
			if (uri == null) return false;
			if (uri.Host.Contains("github.com", StringComparison.OrdinalIgnoreCase)) return true;
			if (uri.Host.Contains("github.io", StringComparison.OrdinalIgnoreCase)) return true;
			return false;
		}

		public static bool IsUpToDate(this SemVersion currentVersion, SemVersion desiredVersion)
		{
			if (desiredVersion == null) throw new ArgumentNullException(nameof(desiredVersion));

			return currentVersion == null || currentVersion >= desiredVersion;
		}

		// From The Cake Incubator project
		public static string GetFirstElementValue(this XDocument document, XName elementName, string config = null, string platform = "AnyCPU")
		{
			var elements = document.Descendants(elementName);
			if (!elements.Any())
			{
				return null;
			}

			if (string.IsNullOrEmpty(config))
			{
				return elements.FirstOrDefault((XElement x) => !x.WithConfigCondition())?.Value;
			}

			return elements.FirstOrDefault((XElement x) => x.WithConfigCondition(config, platform))?.Value ?? elements.FirstOrDefault((XElement x) => !x.WithConfigCondition())?.Value;
		}

		// From The Cake Incubator project
		public static bool WithConfigCondition(this XElement element, string config = null, string platform = null)
		{
			bool? configAttribute = element.Attribute("Condition")?.Value.HasConfigPlatformCondition(config, platform);
			if (!configAttribute.HasValue)
			{
				configAttribute = element.Parent?.Attribute("Condition")?.Value.HasConfigPlatformCondition(config, platform);
			}

			return configAttribute ?? false;
		}

		// From The Cake Incubator project
		public static bool HasConfigPlatformCondition(this string condition, string config = null, string platform = null)
		{
			return string.IsNullOrEmpty(config) ? condition.StartsWith("'$(Configuration)|$(Platform)'==") : condition.EqualsIgnoreCase("'$(Configuration)|$(Platform)'=='" + config + "|" + platform + "'");
		}

		public static bool SetFirstElementValue(this XDocument document, XName elementName, string newValue, string config = null, string platform = "AnyCPU")
		{
			var elements = document.Descendants(elementName);
			if (!elements.Any()) return false;

			XElement element = null;
			if (string.IsNullOrEmpty(config))
			{
				element = elements.FirstOrDefault((XElement x) => !x.WithConfigCondition());
			}
			else
			{
				element = elements.FirstOrDefault((XElement x) => x.WithConfigCondition(config, platform)) ?? elements.FirstOrDefault((XElement x) => !x.WithConfigCondition());
			}

			if (element == null) return false;

			element.SetValue(newValue);
			return true;
		}

		// From The Cake Incubator project
		public static XName GetXNameWithNamespace(this XNamespace ns, string elementName)
		{
			string nsName = ns?.NamespaceName;
			return (nsName == null) ? XName.Get(elementName) : XName.Get(elementName, nsName);
		}

		public static IEnumerable<KeyValuePair<string, string>> ParseQuerystring(this Uri uri)
		{
			var querystringParameters = uri
				.Query.TrimStart('?')
				.Split(new char[] { '&' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(value => value.Split(new char[] { '=' }, StringSplitOptions.RemoveEmptyEntries))
				.Select(splitValue =>
				{
					if (splitValue.Length == 1)
					{
						return new KeyValuePair<string, string>(splitValue[0].Trim(), null);
					}
					else
					{
						return new KeyValuePair<string, string>(splitValue[0].Trim(), splitValue[1].Trim());
					}
				});

			return querystringParameters;
		}

		public static string GetChildNodeValue(this YamlMappingNode mapping, string name)
		{
			var key = new YamlScalarNode(name);
			if (!mapping.Children.ContainsKey(key)) return string.Empty;
			return mapping.Children[key].ToString();
		}

		private static void CheckIsEnum<T>(bool withFlags)
		{
			if (!typeof(T).IsEnum)
				throw new ArgumentException($"Type '{typeof(T).FullName}' is not an enum");
			if (withFlags && !Attribute.IsDefined(typeof(T), typeof(FlagsAttribute)))
				throw new ArgumentException($"Type '{typeof(T).FullName}' doesn't have the 'Flags' attribute");
		}
	}
}
