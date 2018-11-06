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
		public static readonly Regex AddinReferenceRegex = new Regex("(?<lineprefix>.*?)(?<packageprefix>\\#(addin|tool) nuget:\\?)(?<referencestring>.*?(?=(?:\")|$))(?<linepostfix>.*)", RegexOptions.Compiled | RegexOptions.Multiline);

		private IEnumerable<AddinReference> _addinReferences;

		public string Name { get; set; }

		public string Path { get; set; }

		public string Content { get; set; }

		public IEnumerable<AddinReference> AddinReferences
		{
			get
			{
				if (_addinReferences == null)
				{
					var references = new List<AddinReference>();
					var matchResults = RecipeFile.AddinReferenceRegex.Matches(Content);
					foreach (Match match in matchResults)
					{
						var parameters = HttpUtility.ParseQueryString(match.Groups["referencestring"].Value);

						var packageName = parameters["package"];
						var referencedVersion = parameters["version"];

						references.Add(new AddinReference()
						{
							Name = packageName,
							ReferencedVersion = referencedVersion,
							LatestVersionForCurrentCake = null,
							LatestVersionForLatestCake = null
						});
					}

					_addinReferences = references
						.OrderBy(r => r.Name)
						.ToArray();
				}

				return _addinReferences;
			}
		}

		public string GetContentForCurrentCake()
		{
			return GetContent(Content, addin => addin.LatestVersionForCurrentCake);
		}

		public string GetContentForLatestCake()
		{
			return GetContent(Content, addin => addin.LatestVersionForLatestCake);
		}

		private string GetContent(string content, Func<AddinReference, string> getAddinVersion)
		{
			var updatedContent = AddinReferenceRegex.Replace(content, match =>
			{
				var parameters = HttpUtility.ParseQueryString(match.Groups["referencestring"].Value);

				// These are the supported parameters as documented here: https://cakebuild.net/docs/fundamentals/preprocessor-directives
				var packageName = parameters["package"];
				var referencedVersion = parameters["version"];
				var loadDependencies = parameters["loaddependencies"];
				var include = parameters["include"];
				var exclude = parameters["exclude"];
				var prerelease = parameters.AllKeys.Contains("prerelease");

				var referencedAddin = AddinReferences.Where(addin => addin.Name.Equals(packageName, StringComparison.OrdinalIgnoreCase));
				if (!referencedAddin.Any()) return match.Groups[0].Value;

				var updatedVersion = getAddinVersion(referencedAddin.First());
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
