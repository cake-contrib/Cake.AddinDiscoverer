using OfficeOpenXml.Style;
using System;
using System.Drawing;

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
		public const string GREEN_EMOJI = ":white_check_mark: ";
		public const string RED_EMOJI = ":small_red_triangle: ";
		public const string YELLOW_EMOJI = ":warning: ";
		public const string CSV_DATE_FORMAT = "yyyy-MM-dd HH:mm:ss";
		public const string PACKAGES_CONFIG_PATH = "tools/packages.config";

		public const string CAKE_REPO_OWNER = "cake-build";
		public const string CAKE_WEBSITE_REPO_NAME = "website";
		public const string CAKE_CONTRIB_REPO_OWNER = "cake-contrib";
		public const string CAKE_CONTRIB_REPO_NAME = "Home";
		public const string CAKE_RECIPE_REPO_NAME = "Cake.Recipe";

		public static readonly SemVersion UNKNOWN_VERSION = new SemVersion(0, 0, 0);

		public static readonly CakeVersion[] CAKE_VERSIONS = new[]
		{
			new CakeVersion
			{
				Version = new SemVersion(0, 26, 0),
				RequiredFramework = "netstandard2.0",
				OptionalFramework = "net46"
			},
			new CakeVersion
			{
				Version = new SemVersion(0, 28, 0),
				RequiredFramework = "netstandard2.0",
				OptionalFramework = "net46"
			},
			new CakeVersion
			{
				Version = new SemVersion(0, 33, 0),
				RequiredFramework = "netstandard2.0",
				OptionalFramework = "net46"
			}
		};

#pragma warning disable SA1000 // Keywords should be spaced correctly
#pragma warning disable SA1008 // Opening parenthesis should be spaced correctly
#pragma warning disable SA1009 // Closing parenthesis should be spaced correctly
		public static readonly (string Header, ExcelHorizontalAlignment Align, Func<AddinMetadata, object> GetContent, Func<AddinMetadata, CakeVersion, Color> GetCellColor, Func<AddinMetadata, Uri> GetHyperLink, AddinType ApplicableTo, DataDestination Destination)[] REPORT_COLUMNS = new (string Header, ExcelHorizontalAlignment Align, Func<AddinMetadata, object> GetContent, Func<AddinMetadata, CakeVersion, Color> GetCellColor, Func<AddinMetadata, Uri> GetHyperLink, AddinType ApplicableTo, DataDestination Destination)[]
		{
			(
				"Name",
				ExcelHorizontalAlignment.Left,
				(addin) => addin.Name,
				(addin, cakeVersion) => Color.Empty,
				(addin) => addin.ProjectUrl ?? addin.NuGetPackageUrl ?? addin.RepositoryUrl,
				AddinType.All,
				DataDestination.All
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
				"Cake Core Version",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.CakeCoreVersion == null ? string.Empty : addin.AnalysisResult.CakeCoreVersion == Constants.UNKNOWN_VERSION ? "Unknown" : addin.AnalysisResult.CakeCoreVersion.ToString(3),
				(addin, cakeVersion) => addin.AnalysisResult.CakeCoreVersion == null ? Color.Empty : (addin.AnalysisResult.CakeCoreVersion.IsUpToDate(cakeVersion.Version) ? Color.LightGreen : Color.Red),
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
				(addin) => addin.AnalysisResult.CakeCommonVersion == null ? string.Empty : addin.AnalysisResult.CakeCommonVersion == Constants.UNKNOWN_VERSION ? "Unknown" : addin.AnalysisResult.CakeCommonVersion.ToString(3),
				(addin, cakeVersion) => addin.AnalysisResult.CakeCommonVersion == null ? Color.Empty : (addin.AnalysisResult.CakeCommonVersion.IsUpToDate(cakeVersion.Version) ? Color.LightGreen : Color.Red),
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
				(addin) => string.Join(", ", addin.Frameworks),
				(addin, cakeVersion) => (addin.Frameworks ?? Array.Empty<string>()).Length == 0 ? Color.Empty : (Misc.IsFrameworkUpToDate(addin.Frameworks, cakeVersion) ? Color.LightGreen : Color.Red),
				(addin) => null,
				AddinType.Addin | AddinType.Module,
				DataDestination.All
			),
			(
				"Icon",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.UsingNewCakeContribIcon.ToString().ToLower(),
				(addin, cakeVersion) => addin.AnalysisResult.UsingNewCakeContribIcon ? Color.LightGreen : Color.Red,
				(addin) => null,
				AddinType.All,
				DataDestination.Excel
			),
			(
				"Icon",
				ExcelHorizontalAlignment.Center,
				(addin) => string.Empty,
				(addin, cakeVersion) => addin.AnalysisResult.UsingNewCakeContribIcon ? Color.LightGreen : Color.Red,
				(addin) => null,
				AddinType.All,
				DataDestination.MarkdownForRecipes // This column not displayed in markdown for addins due to space restriction
			),
			(
				"Transferred to cake-contrib",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.TransferredToCakeContribOrganisation.ToString().ToLower(),
				(addin, cakeVersion) => addin.AnalysisResult.TransferredToCakeContribOrganisation ? Color.LightGreen : Color.Red,
				(addin) => null,
				AddinType.All,
				DataDestination.Excel
			),
			(
				"Transferred to cake-contrib",
				ExcelHorizontalAlignment.Center,
				(addin) => string.Empty,
				(addin, cakeVersion) => addin.AnalysisResult.TransferredToCakeContribOrganisation ? Color.LightGreen : Color.Red,
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
				DataDestination.Excel
			),
			(
				"Repository",
				ExcelHorizontalAlignment.Center,
				(addin) => string.Empty,
				(addin, cakeVersion) => !addin.AnalysisResult.RepositoryInfoProvided ? Color.Red : (addin.RepositoryUrl.AbsolutePath.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? Color.LightGreen : Color.Gold),
				(addin) => null,
				AddinType.All,
				DataDestination.MarkdownForRecipes // This column not displayed in markdown for addins due to space restriction
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
				(addin, cakeVersion) => Color.Empty,
				(addin) => null,
				AddinType.All,
				DataDestination.Excel
			),
			(
				"Pull requests count",
				ExcelHorizontalAlignment.Center,
				(addin) => addin.AnalysisResult.OpenPullRequestsCount,
				(addin, cakeVersion) => Color.Empty,
				(addin) => null,
				AddinType.All,
				DataDestination.Excel
			),
			(
				"Cake.Recipe",
				ExcelHorizontalAlignment.Center,
				(addin) => !addin.AnalysisResult.CakeRecipeIsUsed ? "Not using Cake.Recipe" : (addin.AnalysisResult.CakeRecipeVersion ?? "Unspecified version") + (addin.AnalysisResult.CakeRecipePrerelease ? " (prerelease)" : string.Empty),
				(addin, cakeVersion) => !addin.AnalysisResult.CakeRecipeIsUsed ? Color.Red : (addin.AnalysisResult.CakeRecipePrerelease ? Color.Gold : Color.LightGreen),
				(addin) => null,
				AddinType.All,
				DataDestination.Excel
			)
		};
#pragma warning restore SA1009 // Closing parenthesis should be spaced correctly
#pragma warning restore SA1008 // Opening parenthesis should be spaced correctly
#pragma warning restore SA1000 // Keywords should be spaced correctly
	}
}
