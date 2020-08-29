using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace Cake.AddinDiscoverer.Models
{
	internal class RecipeFile
	{
		public static readonly Regex AddinReferenceRegex = new Regex(string.Format(ADDIN_REFERENCE_REGEX, "addin"), RegexOptions.Compiled | RegexOptions.Multiline);
		public static readonly Regex ToolReferenceRegex = new Regex(string.Format(ADDIN_REFERENCE_REGEX, "tool"), RegexOptions.Compiled | RegexOptions.Multiline);
		public static readonly Regex LoadReferenceRegex = new Regex(string.Format(ADDIN_REFERENCE_REGEX, "(load|l)"), RegexOptions.Compiled | RegexOptions.Multiline);

		private const string ADDIN_REFERENCE_REGEX = "(?<lineprefix>.*)(?<packageprefix>\\#{0}) (?<scheme>(nuget|dotnet)):(?<separator1>\"?)(?<packagerepository>.*)\\?(?<referencestring>.*?(?=(?:[\"| ])|$))(?<separator2>\"?)(?<separator3> ?)(?<linepostfix>.*?$)";

		private IEnumerable<AddinReference> _addinReferences;

		private IEnumerable<ToolReference> _toolReferences;

		private IEnumerable<CakeReference> _loadReferences;

		public string Name { get; set; }

		public string Path { get; set; }

		public string Content { get; set; }

		public IEnumerable<AddinReference> AddinReferences
		{
			get
			{
				if (_addinReferences == null)
				{
					// For the purpose of this automation we are only considering addins that follow the recommended naming convention which
					// is necessary in order to ignore references such as: #addin nuget:?package=RazorEngine&version=3.10.0&loaddependencies=true
					// This has the potential of overlooking references to legitimate Cake addin if they don't follow the naming convention.
					// I think this is an acceptable risk because, as of this writing, there is only one addin that I know of that is not
					// following the naming guideline: Magic-Chunks.
					_addinReferences = FindReferences<AddinReference>(Content, RecipeFile.AddinReferenceRegex, true);
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
					_toolReferences = FindReferences<ToolReference>(Content, RecipeFile.ToolReferenceRegex, false);
				}

				return _toolReferences;
			}
		}

		public IEnumerable<CakeReference> LoadReferences
		{
			get
			{
				if (_loadReferences == null)
				{
					_loadReferences = FindReferences<ToolReference>(Content, RecipeFile.LoadReferenceRegex, false);
				}

				return _loadReferences;
			}
		}

		public string GetContentForCurrentCake()
		{
			var updatedContent = GetContent(Content, AddinReferenceRegex, AddinReferences, r => (r as AddinReference).LatestVersionForCurrentCake);
			updatedContent = GetContent(updatedContent, ToolReferenceRegex, ToolReferences, r => (r as ToolReference).LatestVersion);
			return updatedContent;
		}

		public string GetContentForLatestCake()
		{
			var updatedContent = GetContent(Content, AddinReferenceRegex, AddinReferences, r => (r as AddinReference).LatestVersionForLatestCake);
			updatedContent = GetContent(updatedContent, ToolReferenceRegex, ToolReferences, r => (r as ToolReference).LatestVersion);
			return updatedContent;
		}

		public string GetContentForCurrentCake(CakeReference reference)
		{
			var updatedContent = string.Empty;

			if (reference is AddinReference addinReference)
			{
				updatedContent = GetContent(Content, AddinReferenceRegex, new[] { addinReference }, r => addinReference.LatestVersionForCurrentCake);
			}
			else if (reference is ToolReference toolReference)
			{
				updatedContent = GetContent(Content, ToolReferenceRegex, new[] { toolReference }, r => toolReference.LatestVersion);
			}
			else
			{
				throw new ArgumentException("Unknown reference type", nameof(reference));
			}

			return updatedContent;
		}

		private static IEnumerable<T> FindReferences<T>(string content, Regex regex, bool enforceNamingConvention)
			where T : CakeReference, new()
		{
			// Replacing Windows CR+LF with Unix LF is important because '$' in our regex only works with Unix line endings
			var unixFormat = content.Replace("\r\n", "\n");

			var references = new List<T>();
			var matchResults = regex.Matches(unixFormat);

			if (!matchResults.Any()) return Array.Empty<T>();

			foreach (Match match in matchResults)
			{
				var parameters = HttpUtility.ParseQueryString(match.Groups["referencestring"].Value);

				var packageName = parameters["package"];
				var referencedVersion = parameters["version"];
				var prerelease = (parameters.AllKeys?.Contains("prerelease") ?? false) || (parameters.GetValues(null)?.Contains("prerelease") ?? false);

				if (!enforceNamingConvention || packageName.StartsWith("Cake.", StringComparison.OrdinalIgnoreCase))
				{
					references.Add(new T()
					{
						Name = packageName,
						ReferencedVersion = referencedVersion,
						Prerelease = prerelease
					});
				}
			}

			return references
				.OrderBy(r => r.Name)
				.ToArray();
		}

		private string GetContent(string content, Regex regex, IEnumerable<CakeReference> references, Func<CakeReference, string> getUpdatedVersion)
		{
			// Replacing Windows CR+LF with Unix LF is important because '$' in our regex only works with Unix line endings
			var unixFormat = content.Replace("\r\n", "\n");

			var updatedContent = regex.Replace(unixFormat, match =>
			{
				var parameters = HttpUtility.ParseQueryString(match.Groups["referencestring"].Value);

				// These are the supported parameters as documented here: https://cakebuild.net/docs/fundamentals/preprocessor-directives
				var packageName = parameters["package"];
				var referencedVersion = parameters["version"];
				var loadDependencies = parameters["loaddependencies"];
				var include = parameters["include"];
				var exclude = parameters["exclude"];
				var prerelease = (parameters.AllKeys?.Contains("prerelease") ?? false) || (parameters.GetValues(null)?.Contains("prerelease") ?? false);

				var referencedAddin = references.Where(addin => addin.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase));
				if (!referencedAddin.Any()) return match.Groups[0].Value;

				var updatedVersion = getUpdatedVersion(referencedAddin.First());
				if (string.IsNullOrEmpty(updatedVersion)) return match.Groups[0].Value;
				if (referencedVersion == updatedVersion) return match.Groups[0].Value;

				var newContent = new StringBuilder();
				newContent.Append(match.Groups["lineprefix"].Value);
				newContent.Append(match.Groups["packageprefix"].Value);
				newContent.AppendFormat(" {0}:", match.Groups["scheme"].Value);
				newContent.Append(match.Groups["separator1"].Value);
				newContent.Append(match.Groups["packagerepository"].Value);
				newContent.AppendFormat("?package={0}", packageName);
				newContent.AppendFormat("&version={0}", updatedVersion);
				if (!string.IsNullOrEmpty(loadDependencies)) newContent.AppendFormat("&loaddependencies={0}", loadDependencies);
				if (!string.IsNullOrEmpty(include)) newContent.AppendFormat("&include={0}", include);
				if (!string.IsNullOrEmpty(exclude)) newContent.AppendFormat("&exclude={0}", exclude);
				if (prerelease) newContent.Append("&prerelease");
				newContent.Append(match.Groups["separator2"].Value);
				newContent.Append(match.Groups["separator3"].Value);
				newContent.Append(match.Groups["linepostfix"].Value);

				return newContent.ToString();
			});

			return updatedContent;
		}
	}
}
