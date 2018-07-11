using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cake.AddinDiscoverer
{
	internal class RecipeFile
	{
		public static readonly Regex AddinReferenceRegex = new Regex(@"(?<lineprefix>.*?)(?<packageprefix>\#addin nuget:\?package=)(?<packagename>.*)(?<versionprefix>&version=)(?<packageversion>.*)", RegexOptions.Compiled | RegexOptions.Multiline);

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
						var packageName = match.Groups["packagename"].Value;
						var referencedVersion = match.Groups["packageversion"].Value;

						references.Add(new AddinReference()
						{
							Name = packageName,
							ReferencedVersion = referencedVersion,
							LatestVersionForCurrentCake = null,
							LatestVersionForLatestCake = null
						});
					}

					_addinReferences = references.ToArray();
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
				var referencedAddin = AddinReferences.Where(addin => addin.Name.Equals(match.Groups["packagename"].Value, StringComparison.OrdinalIgnoreCase));
				if (!referencedAddin.Any()) return match.Groups[0].Value;

				var updatedVersion = getAddinVersion(referencedAddin.First());
				if (string.IsNullOrEmpty(updatedVersion)) return match.Groups[0].Value;

				if (match.Groups["packageversion"].Value == updatedVersion) return match.Groups[0].Value;

				return $"{match.Groups["lineprefix"].Value}{match.Groups["packageprefix"].Value}{match.Groups["packagename"].Value}{match.Groups["versionprefix"].Value}{updatedVersion}";
			});

			return updatedContent;
		}
	}
}
