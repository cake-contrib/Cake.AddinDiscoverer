using Cake.AddinDiscoverer.Models;
using Cake.Incubator.StringExtensions;
using OfficeOpenXml.Style;
using System;
using System.Drawing;
using System.Linq;

namespace Cake.AddinDiscoverer.Utilities
{
	internal static class Constants
	{
		public const string FILE_MODE = "100644";

		public const string PRODUCT_NAME = "Cake.AddinDiscoverer";
		public const string ISSUE_TITLE = "Recommended changes resulting from automated audit";
		public const string PULL_REQUEST_TITLE = "Fix issues identified by automated audit";
		public const string NEW_CAKE_CONTRIB_ICON_URL = "https://cdn.jsdelivr.net/gh/cake-contrib/graphics/png/cake-contrib-medium.png";
		public const string OLD_CAKE_CONTRIB_ICON_URL = "https://cdn.rawgit.com/cake-contrib/graphics/a5cf0f881c390650144b2243ae551d5b9f836196/png/cake-contrib-medium.png";
		public const string CAKE_RECIPE_UPGRADE_CAKE_VERSION_ISSUE_TITLE = "Support Cake {0}";
		public const int MAX_GITHUB_CONCURENCY = 10;
		public const int MAX_NUGET_CONCURENCY = 25; // I suspect nuget allows a much large number of concurrent connections but 25 seems like a safe value.
		public const string GREEN_EMOJI = ":heavy_check_mark: ";
		public const string RED_EMOJI = ":x: ";
		public const string YELLOW_EMOJI = ":warning: ";
		public const string CSV_DATE_FORMAT = "yyyy-MM-dd HH:mm:ss";
		public const string DOT_NET_TOOLS_CONFIG_PATH = ".config/dotnet-tools.json";
		public const string CAKE_VERSION_YML_PATH = "Source/Cake.Recipe/cake-version.yml";
		public const string NO_DESCRIPTION_PROVIDED = "No description has been provided";
		public const string COLLECTIVE_YAML_SYNCHRONIZATION_ISSUE_TITLE = "Synchronize YAML files";
		public const string CONTRIBUTORS_SYNCHRONIZATION_ISSUE_TITLE = "Synchronize Contributors";

		public const string CAKE_CONTRIB_ADDIN_FANCY_ICON_URL = "https://cdn.jsdelivr.net/gh/cake-contrib/graphics/png/addin/cake-contrib-addin-medium.png";
		public const string CAKE_CONTRIB_MODULE_FANCY_ICON_URL = "https://cdn.jsdelivr.net/gh/cake-contrib/graphics/png/module/cake-contrib-module-medium.png";
		public const string CAKE_CONTRIB_RECIPE_FANCY_ICON_URL = "https://cdn.jsdelivr.net/gh/cake-contrib/graphics/png/recipe/cake-contrib-recipe-medium.png";
		public const string CAKE_CONTRIB_FROSTINGRECIPE_FANCY_ICON_URL = "https://cdn.jsdelivr.net/gh/cake-contrib/graphics/png/frosting-recipe/cake-contrib-frosting-recipe-medium.png";
		public const string CAKE_CONTRIB_COMMUNITY_FANCY_ICON_URL = "https://cdn.jsdelivr.net/gh/cake-contrib/graphics/png/community/cake-contrib-community-medium.png";

		// Stop commiting changes, raising issues and submitting PRs if the number of remaining API calls is below a safe threshold.
		// This threshold is arbitrary but I set it to a value that is high enough to hopefully avoid 'AbuseException'.
		// Keep in mind that in many cases we have multiple concurrent connections making a multitude of calls to GihHub's API
		// so this number must be large enough to allow us to bail out before we exhaust the calls we are allowed to make in an hour
		// Having said this, keep in mind that even with a high 'threshold' we can still trigger the abuse detection if we commit
		// changes, raise issues and submit PRs too quickly.
		public const int MIN_GITHUB_REQUESTS_THRESHOLD = 250;

		public const string CAKE_REPO_OWNER = "cake-build";
		public const string CAKE_WEBSITE_REPO_NAME = "website";
		public const string CAKE_CONTRIB_REPO_OWNER = "cake-contrib";
		public const string CAKE_CONTRIB_REPO_NAME = "Home";
		public const string CAKE_RECIPE_REPO_NAME = "Cake.Recipe";
		public const string ADDIN_DISCOVERER_REPO_NAME = "Cake.AddinDiscoverer";

		public static readonly DateTime UtcMinDateTime = new DateTime(0, DateTimeKind.Utc); // <== Making sure the timezone is UTC. Do not use DateTime.MinValue because the 'Kind' is unspecified.
		public static readonly SemVersion VERSION_UNKNOWN = new SemVersion(-1, 0, 0);
		public static readonly SemVersion VERSION_ZERO = new SemVersion(0, 0, 0);

		public static readonly CakeVersion[] CAKE_VERSIONS = new[]
		{
			new CakeVersion // This represents all versions of Cake prior to 1.0.0. For example: 0.36.0
			{
				Version = VERSION_ZERO,
				RequiredFrameworks = Array.Empty<string>(),
				OptionalFrameworks = Array.Empty<string>()
			},
			new CakeVersion
			{
				Version = new SemVersion(1, 0, 0),
				RequiredFrameworks = new[] { "netstandard2.0" },
				OptionalFrameworks = new[] { "net46", "net461", "net5.0" }
			},
			new CakeVersion
			{
				Version = new SemVersion(2, 0, 0),
				RequiredFrameworks = new[] { "net6.0", "net5.0", "netcoreapp3.1" },
				OptionalFrameworks = Array.Empty<string>()
			},
			new CakeVersion
			{
				Version = new SemVersion(3, 0, 0),
				RequiredFrameworks = new[] { "net6.0", "net7.0" },
				OptionalFrameworks = Array.Empty<string>()
			}
		};

#pragma warning disable SA1009 // Closing parenthesis should be spaced correctly
		public static readonly (string Header, ExcelHorizontalAlignment Align, Func<AddinMetadata, object> GetContent, Func<AddinMetadata, CakeVersion, Color> GetCellColor, Func<AddinMetadata, Uri> GetHyperLink, AddinType ApplicableTo, DataDestination Destination)[] REPORT_COLUMNS = new (string Header, ExcelHorizontalAlignment Align, Func<AddinMetadata, object> GetContent, Func<AddinMetadata, CakeVersion, Color> GetCellColor, Func<AddinMetadata, Uri> GetHyperLink, AddinType ApplicableTo, DataDestination Destination)[]
		{
			(
				"Name",
				ExcelHorizontalAlignment.Left,
				(addin) => addin.Name,
				(addin, cakeVersion) => Color.Empty,
				(addin) => addin.ProjectUrl,
				AddinType.All,
				DataDestination.Excel
			),
			(
				"Name",
				ExcelHorizontalAlignment.Left,
				(addin) => $"{addin.Name} {addin.NuGetPackageVersion}",
				(addin, cakeVersion) => Color.Empty,
				(addin) => addin.ProjectUrl,
				AddinType.All,
				DataDestination.All & ~DataDestination.Excel
			),
			(
				"NuGet package version",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.NuGetPackageVersion,
				(addin, cakeVersion) => Color.Empty,
				(addin) => addin.NuGetPackageUrl == null ? null : new Uri(addin.NuGetPackageUrl, addin.NuGetPackageVersion),
				AddinType.All,
				DataDestination.Excel
			),
			(
				"Type",
				ExcelHorizontalAlignment.Left,
				(addin) => addin.Type,
				(addin, cakeVersion) => Color.Empty,
				(addin) => null,
				AddinType.Addin | AddinType.Module,
				DataDestination.Excel
			),
			(
				"Maintainer",
				ExcelHorizontalAlignment.Left,
				(addin) => addin.GetMaintainerName(),
				(addin, cakeVersion) => Color.Empty,
				(addin) => null,
				AddinType.All,
				DataDestination.Excel | DataDestination.MarkdownForRecipes
			),
			(
				"Target Cake Version",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.CakeVersionYaml?.TargetCakeVersion?.ToString(3) ?? string.Empty,
				(addin, cakeVersion) =>
				{
					if (addin.CakeVersionYaml?.TargetCakeVersion == null) return Color.Red;

					var comparisonResult = addin.CakeVersionYaml.TargetCakeVersion.CompareTo(cakeVersion.Version);
					if (comparisonResult > 0) return Color.Gold;
					else if (comparisonResult == 0) return Color.LightGreen;
					else return Color.Red;
				},
				(addin) => null,
				AddinType.Recipe,
				DataDestination.Excel | DataDestination.MarkdownForRecipes
			),
			(
				"Cake Core Version",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.CakeCoreVersion == null ? string.Empty : addin.AnalysisResult.CakeCoreVersion == Constants.VERSION_UNKNOWN ? "Unknown" : addin.AnalysisResult.CakeCoreVersion == Constants.VERSION_ZERO ? "Pre 1.0.0" : addin.AnalysisResult.CakeCoreVersion.ToString(3),
				(addin, cakeVersion) =>
				{
					if (addin.AnalysisResult.CakeCoreVersion == null) return Color.Empty;

					var comparisonResult = addin.AnalysisResult.CakeCoreVersion.CompareTo(cakeVersion.Version);
					if (comparisonResult > 0) return Color.Gold;
					else if (comparisonResult == 0) return Color.LightGreen;
					else return Color.Red;
				},
				(addin) => null,
				AddinType.Addin | AddinType.Module,
				DataDestination.All
			),
			(
				"Cake Core IsPrivate",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.CakeCoreVersion == null ? string.Empty : addin.AnalysisResult.CakeCoreIsPrivate.ToString().ToLower(),
				(addin, cakeVersion) => addin.AnalysisResult.CakeCoreVersion == null ? Color.Empty : (addin.AnalysisResult.CakeCoreIsPrivate ? Color.LightGreen : Color.Red),
				(addin) => null,
				AddinType.Addin | AddinType.Module,
				DataDestination.Excel
			),
			(
				"Cake Core IsPrivate",
				ExcelHorizontalAlignment.Center,
				(addin) => string.Empty,
				(addin, cakeVersion) => addin.AnalysisResult.CakeCoreVersion == null ? Color.Empty : (addin.AnalysisResult.CakeCoreIsPrivate ? Color.LightGreen : Color.Red),
				(addin) => null,
				AddinType.Addin | AddinType.Module,
				DataDestination.All & ~DataDestination.Excel
			),
			(
				"Cake Common Version",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.CakeCommonVersion == null ? string.Empty : addin.AnalysisResult.CakeCommonVersion == Constants.VERSION_UNKNOWN ? "Unknown" : addin.AnalysisResult.CakeCommonVersion == Constants.VERSION_ZERO ? "Pre 1.0.0" : addin.AnalysisResult.CakeCommonVersion.ToString(3),
				(addin, cakeVersion) =>
				{
					if (addin.AnalysisResult.CakeCommonVersion == null) return Color.Empty;

					var comparisonResult = addin.AnalysisResult.CakeCommonVersion.CompareTo(cakeVersion.Version);
					if (comparisonResult > 0) return Color.Gold;
					else if (comparisonResult == 0) return Color.LightGreen;
					else return Color.Red;
				},
				(addin) => null,
				AddinType.Addin | AddinType.Module,
				DataDestination.All
			),
			(
				"Cake Common IsPrivate",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.CakeCommonVersion == null ? string.Empty : addin.AnalysisResult.CakeCommonIsPrivate.ToString().ToLower(),
				(addin, cakeVersion) => addin.AnalysisResult.CakeCommonVersion == null ? Color.Empty : (addin.AnalysisResult.CakeCommonIsPrivate ? Color.LightGreen : Color.Red),
				(addin) => null,
				AddinType.Addin | AddinType.Module,
				DataDestination.Excel
			),
			(
				"Cake Common IsPrivate",
				ExcelHorizontalAlignment.Center,
				(addin) => string.Empty,
				(addin, cakeVersion) => addin.AnalysisResult.CakeCommonVersion == null ? Color.Empty : (addin.AnalysisResult.CakeCommonIsPrivate ? Color.LightGreen : Color.Red),
				(addin) => null,
				AddinType.Addin | AddinType.Module,
				DataDestination.All & ~DataDestination.Excel
			),
			(
				"Framework",
				ExcelHorizontalAlignment.Center,
				(addin) => string.Join(", ", addin.Frameworks ?? Array.Empty<string>()),
				(addin, cakeVersion) => (addin.Frameworks ?? Array.Empty<string>()).Length == 0 ? Color.Empty : (Misc.IsFrameworkUpToDate(addin.Frameworks, cakeVersion) ? Color.LightGreen : Color.Red),
				(addin) => null,
				AddinType.Addin | AddinType.Module,
				DataDestination.All
			),
			(
				"Icon",
				ExcelHorizontalAlignment.Center,
				(addin) =>
				{
					return addin.AnalysisResult.Icon switch
					{
						IconAnalysisResult.Unspecified => "not specified",
						IconAnalysisResult.RawgitUrl => "rawgit",
						IconAnalysisResult.JsDelivrUrl => "jsDelivr",
						IconAnalysisResult.CustomUrl => "custom url",
						IconAnalysisResult.EmbeddedCakeContrib => "embedded cake-contrib",
						IconAnalysisResult.EmbeddedFancyCakeContrib => "embedded 'fancy' cake-contrib",
						IconAnalysisResult.EmbeddedCustom => "embedded custom",
						_ => "unknown"
					};
				},
				(addin, cakeVersion) =>
								{
					return addin.AnalysisResult.Icon switch
					{
						IconAnalysisResult.Unspecified => Color.Red,
						IconAnalysisResult.RawgitUrl => Color.Red,
						IconAnalysisResult.JsDelivrUrl => Color.Gold,
						IconAnalysisResult.CustomUrl => Color.Gold,
						IconAnalysisResult.EmbeddedCakeContrib => Color.LightGreen,
						IconAnalysisResult.EmbeddedFancyCakeContrib => Color.LightGreen,
						IconAnalysisResult.EmbeddedCustom => Color.Gold,
						_ => Color.Red
					};
				},
				(addin) => null,
				AddinType.All,
				DataDestination.Excel | DataDestination.MarkdownForRecipes // This column not displayed in markdown for addins due to space restriction
			),
			(
				"Transferred to cake-contrib",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.TransferredToCakeContribOrganization.ToString().ToLower(),
				(addin, cakeVersion) => addin.AnalysisResult.TransferredToCakeContribOrganization ? Color.LightGreen : Color.Red,
				(addin) => null,
				AddinType.All,
				DataDestination.Excel
			),
			(
				"Transferred to cake-contrib",
				ExcelHorizontalAlignment.Center,
				(addin) => string.Empty,
				(addin, cakeVersion) => addin.AnalysisResult.TransferredToCakeContribOrganization ? Color.LightGreen : Color.Red,
				(addin) => null,
				AddinType.All,
				DataDestination.MarkdownForRecipes // This column not displayed in markdown for addins due to space restriction
			),
			(
				"License",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.License,
				(addin, cakeVersion) => addin.AnalysisResult.ObsoleteLicenseUrlRemoved ? Color.LightGreen : Color.Red,
				(addin) => null,
				AddinType.All,
				DataDestination.Excel | DataDestination.MarkdownForRecipes // This column not displayed in markdown for addins due to space restriction
			),
			(
				"Repository",
				ExcelHorizontalAlignment.Center,
				(addin) => !addin.AnalysisResult.RepositoryInfoProvided ? "false" : (addin.RepositoryUrl.AbsolutePath.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? "true" : ".git missing"),
				(addin, cakeVersion) => !addin.AnalysisResult.RepositoryInfoProvided ? Color.Red : (addin.RepositoryUrl.AbsolutePath.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? Color.LightGreen : Color.Gold),
				(addin) => null,
				AddinType.All,
				DataDestination.Excel | DataDestination.MarkdownForRecipes // This column not displayed in markdown for addins due to space restriction
			),
			(
				"cake-contrib co-owner",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.PackageCoOwnedByCakeContrib.ToString().ToLower(),
				(addin, cakeVersion) => addin.AnalysisResult.PackageCoOwnedByCakeContrib ? Color.LightGreen : Color.Red,
				(addin) => null,
				AddinType.All,
				DataDestination.Excel
			),
			(
				"Issues count",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.OpenIssuesCount,
				(addin, cakeVersion) =>
				{
					if (!addin.AnalysisResult.OpenIssuesCount.HasValue) return Color.Empty;
					else if (addin.AnalysisResult.OpenIssuesCount.Value < 5) return Color.LightGreen;
					else if (addin.AnalysisResult.OpenIssuesCount.Value < 10) return Color.Gold;
					else return Color.Red;
				},
				(addin) => null,
				AddinType.All,
				DataDestination.Excel
			),
			(
				"Pull requests count",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.OpenPullRequestsCount,
				(addin, cakeVersion) =>
				{
					if (!addin.AnalysisResult.OpenPullRequestsCount.HasValue) return Color.Empty;
					else if (addin.AnalysisResult.OpenPullRequestsCount.Value < 5) return Color.LightGreen;
					else if (addin.AnalysisResult.OpenPullRequestsCount.Value < 10) return Color.Gold;
					else return Color.Red;
				},
				(addin) => null,
				AddinType.All,
				DataDestination.Excel
			),
			(
				"Cake.Recipe",
				ExcelHorizontalAlignment.Center,
				(addin) => !addin.AnalysisResult.CakeRecipeIsUsed ? "Not using Cake.Recipe" : (addin.AnalysisResult.CakeRecipeVersion?.ToString() ?? "Unspecified version") + (addin.AnalysisResult.CakeRecipeIsPrerelease ? " (prerelease)" : string.Empty),
				(addin, cakeVersion) =>
				{
					if (!addin.AnalysisResult.CakeRecipeIsUsed) return Color.Empty;
					else if (addin.AnalysisResult.CakeRecipeIsPrerelease) return Color.Red;
					else if (addin.AnalysisResult.CakeRecipeVersion.Major == 0) return Color.Red;
					else if (addin.AnalysisResult.CakeRecipeIsLatest) return Color.LightGreen;
					else return Color.Gold;
				},
				(addin) => null,
				AddinType.All,
				DataDestination.Excel
			),
			(
				"Newtonsoft.Json",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.References?.FirstOrDefault(r => r.Id.EqualsIgnoreCase("Newtonsoft.Json"))?.Version.ToString(),
				(addin, cakeVersion) => Color.Empty,
				(addin) => null,
				AddinType.All,
				DataDestination.Excel
			),
			(
				"Symbols",
				ExcelHorizontalAlignment.Center,
				(addin) =>
				{
					return addin.PdbStatus switch
					{
						PdbStatus.Embedded => "embedded",
						PdbStatus.IncludedInPackage => "included in nupkg",
						PdbStatus.IncludedInSymbolsPackage => "included in snupkg",
						PdbStatus.NotAvailable => "unavailable",
						_ => "unknown"
					};
				},
				(addin, cakeVersion) =>
				{
					return addin.PdbStatus switch
					{
						PdbStatus.Embedded => Color.LightGreen,
						PdbStatus.IncludedInPackage => Color.LightGreen,
						PdbStatus.IncludedInSymbolsPackage => Color.Gold,
						_ => Color.Red
					};
				},
				(addin) => null,
				AddinType.Addin | AddinType.Module,
				DataDestination.Excel
			),
			(
				"SourceLink",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.SourceLinkEnabled.ToString().ToLower(),
				(addin, cakeVersion) => addin.SourceLinkEnabled ? Color.LightGreen : Color.Red,
				(addin) => null,
				AddinType.Addin | AddinType.Module,
				DataDestination.Excel
			),
			(
				"XML Documentation",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.XmlDocumentationAvailable.ToString().ToLower(),
				(addin, cakeVersion) => addin.XmlDocumentationAvailable ? Color.LightGreen : Color.Red,
				(addin) => null,
				AddinType.Addin | AddinType.Module,
				DataDestination.Excel
			),
			(
				"Alias Categories",
				ExcelHorizontalAlignment.Center,
				(addin) => string.Join(", ", addin.AliasCategories),
				(addin, cakeVersion) => addin.AliasCategories.Any() ? Color.LightGreen : Color.Red,
				(addin) => null,
				AddinType.Addin | AddinType.Module,
				DataDestination.Excel
			)
		};
#pragma warning restore SA1009 // Closing parenthesis should be spaced correctly
	}
}
