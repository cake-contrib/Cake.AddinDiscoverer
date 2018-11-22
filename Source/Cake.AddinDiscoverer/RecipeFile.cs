using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace Cake.AddinDiscoverer
{
	internal class RecipeFile
	{
		public static readonly Regex AddinReferenceRegex = new Regex(string.Format(ADDIN_REFERENCE_REGEX, "addin"), RegexOptions.Compiled | RegexOptions.Multiline);
		public static readonly Regex ToolReferenceRegex = new Regex(string.Format(ADDIN_REFERENCE_REGEX, "tool"), RegexOptions.Compiled | RegexOptions.Multiline);

		private const string ADDIN_REFERENCE_REGEX = "(?<lineprefix>.*?)(?<packageprefix>\\#{0} nuget:\\?)(?<referencestring>.*?(?=(?:\")|$))(?<linepostfix>.*)";

		private IEnumerable<AddinReference> _addinReferences;

		private IEnumerable<ToolReference> _toolReferences;

		public string Name { get; set; }

		public string Path { get; set; }

		public string Content { get; set; }

		public IEnumerable<AddinReference> AddinReferences
		{
			get
			{
				if (_addinReferences == null)
				{
					_addinReferences = FindReferences<AddinReference>(Content, RecipeFile.AddinReferenceRegex);
				}

				return _addinReferences;
			}
		}

		public IEnumerable<ToolReference> ToolReferences
		{
			get
			{
				if (_toolReferences == null)
				{
					_toolReferences = FindReferences<ToolReference>(Content, RecipeFile.ToolReferenceRegex);
				}

				return _toolReferences;
			}
		}

		public string GetContentForCurrentCake()
		{
			var updatedContent = GetContent(Content, AddinReferenceRegex, AddinReferences, reference => (reference as AddinReference).LatestVersionForCurrentCake);
			updatedContent = GetContent(updatedContent, ToolReferenceRegex, ToolReferences, reference => (reference as ToolReference).LatestVersion);
			return updatedContent;
		}

		public string GetContentForLatestCake()
		{
			var updatedContent = GetContent(Content, AddinReferenceRegex, AddinReferences, reference => (reference as AddinReference).LatestVersionForLatestCake);
			updatedContent = GetContent(updatedContent, ToolReferenceRegex, ToolReferences, reference => (reference as ToolReference).LatestVersion);
			return updatedContent;
		}

		private static IEnumerable<T> FindReferences<T>(string content, Regex regex)
			where T : CakeReference, new()
		{
			var references = new List<T>();
			var matchResults = regex.Matches(content);

			if (!matchResults.Any()) return Array.Empty<T>();

			foreach (Match match in matchResults)
			{
				var parameters = HttpUtility.ParseQueryString(match.Groups["referencestring"].Value);

				var packageName = parameters["package"];
				var referencedVersion = parameters["version"];

				references.Add(new T()
				{
					Name = packageName,
					ReferencedVersion = referencedVersion
				});
			}

			return references
				.OrderBy(r => r.Name)
				.ToArray();
		}

		private string GetContent(string content, Regex regex, IEnumerable<CakeReference> references, Func<CakeReference, string> getUpdatedVersion)
		{
			var updatedContent = regex.Replace(content, match =>
			{
				var parameters = HttpUtility.ParseQueryString(match.Groups["referencestring"].Value);

				// These are the supported parameters as documented here: https://cakebuild.net/docs/fundamentals/preprocessor-directives
				var packageName = parameters["package"];
				var referencedVersion = parameters["version"];
				var loadDependencies = parameters["loaddependencies"];
				var include = parameters["include"];
				var exclude = parameters["exclude"];
				var prerelease = parameters.AllKeys.Contains("prerelease");

				var referencedAddin = references.Where(addin => addin.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase));
				if (!referencedAddin.Any()) return match.Groups[0].Value;

				var updatedVersion = getUpdatedVersion(referencedAddin.First());
				if (string.IsNullOrEmpty(updatedVersion)) return match.Groups[0].Value;
				if (referencedVersion == updatedVersion) return match.Groups[0].Value;

				var newContent = new StringBuilder();
				newContent.Append(match.Groups["lineprefix"].Value);
				newContent.Append(match.Groups["packageprefix"].Value);
				newContent.AppendFormat("package={0}", packageName);
				newContent.AppendFormat("&version={0}", updatedVersion);
				if (!string.IsNullOrEmpty(loadDependencies)) newContent.AppendFormat("&loaddependencies={0}", loadDependencies);
				if (!string.IsNullOrEmpty(include)) newContent.AppendFormat("&include={0}", include);
				if (!string.IsNullOrEmpty(exclude)) newContent.AppendFormat("&exclude={0}", exclude);
				if (prerelease) newContent.Append("&prerelease");
				newContent.Append(match.Groups["linepostfix"].Value);

				return newContent.ToString();
			});

			return updatedContent;
		}
	}
}
