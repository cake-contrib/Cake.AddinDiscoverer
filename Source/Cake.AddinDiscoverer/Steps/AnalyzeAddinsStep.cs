using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class AnalyzeAddinsStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => true;

		public string GetDescription(DiscoveryContext context) => "Analyze addins";

		public async Task ExecuteAsync(DiscoveryContext context, TextWriter log, CancellationToken cancellationToken)
		{
			var cakeContribIcon = await context.HttpClient.GetByteArrayAsync(Constants.NEW_CAKE_CONTRIB_ICON_URL).ConfigureAwait(false);
			var fancyIcons = new[]
			{
				new KeyValuePair<AddinType, byte[]>(AddinType.Addin, await context.HttpClient.GetByteArrayAsync(Constants.CAKE_CONTRIB_ADDIN_FANCY_ICON_URL).ConfigureAwait(false)),
				new KeyValuePair<AddinType, byte[]>(AddinType.Module, await context.HttpClient.GetByteArrayAsync(Constants.CAKE_CONTRIB_MODULE_FANCY_ICON_URL).ConfigureAwait(false)),
				new KeyValuePair<AddinType, byte[]>(AddinType.Recipe, await context.HttpClient.GetByteArrayAsync(Constants.CAKE_CONTRIB_RECIPE_FANCY_ICON_URL).ConfigureAwait(false)),
				new KeyValuePair<AddinType, byte[]>(AddinType.Recipe, await context.HttpClient.GetByteArrayAsync(Constants.CAKE_CONTRIB_FROSTINGRECIPE_FANCY_ICON_URL).ConfigureAwait(false)),
				new KeyValuePair<AddinType, byte[]>(AddinType.All, await context.HttpClient.GetByteArrayAsync(Constants.CAKE_CONTRIB_COMMUNITY_FANCY_ICON_URL).ConfigureAwait(false))
			};

			context.Addins = context.Addins
				.Select(addin =>
				{
					addin.AnalysisResult.AtLeastOneDecoratedMethod = addin.DecoratedMethods?.Any() ?? false;

					if (addin.References != null)
					{
						var cakeCommonReference = addin.References.Where(r => r.Id.EqualsIgnoreCase("Cake.Common"));
						if (cakeCommonReference.Any())
						{
							var cakeCommonVersion = cakeCommonReference.Min(r => r.Version);
							var cakeCommonIsPrivate = cakeCommonReference.All(r => r.IsPrivate);
							addin.AnalysisResult.CakeCommonVersion = cakeCommonVersion ?? Constants.UNKNOWN_VERSION;
							addin.AnalysisResult.CakeCommonIsPrivate = cakeCommonIsPrivate;
						}
						else
						{
							addin.AnalysisResult.CakeCommonVersion = null;
							addin.AnalysisResult.CakeCommonIsPrivate = true;
						}

						var cakeCoreReference = addin.References.Where(r => r.Id.EqualsIgnoreCase("Cake.Core"));
						if (cakeCoreReference.Any())
						{
							var cakeCoreVersion = cakeCoreReference.Min(r => r.Version);
							var cakeCoreIsPrivate = cakeCoreReference.All(r => r.IsPrivate);
							addin.AnalysisResult.CakeCoreVersion = cakeCoreVersion ?? Constants.UNKNOWN_VERSION;
							addin.AnalysisResult.CakeCoreIsPrivate = cakeCoreIsPrivate;
						}
						else
						{
							addin.AnalysisResult.CakeCoreVersion = null;
							addin.AnalysisResult.CakeCoreIsPrivate = true;
						}
					}

					if (addin.Type == AddinType.Addin && addin.AnalysisResult.CakeCoreVersion == null && addin.AnalysisResult.CakeCommonVersion == null)
					{
						addin.AnalysisResult.Notes += $"This addin seems to be referencing neither Cake.Core nor Cake.Common.{Environment.NewLine}";
					}

					addin.AnalysisResult.Icon = AnalyzeIcon(addin, cakeContribIcon, fancyIcons);
					addin.AnalysisResult.TransferredToCakeContribOrganization = addin.RepositoryOwner?.Equals(Constants.CAKE_CONTRIB_REPO_OWNER, StringComparison.OrdinalIgnoreCase) ?? false;
					addin.AnalysisResult.ObsoleteLicenseUrlRemoved = !string.IsNullOrEmpty(addin.License);
					addin.AnalysisResult.RepositoryInfoProvided = addin.RepositoryUrl != null;
					addin.AnalysisResult.PackageCoOwnedByCakeContrib = addin.NuGetPackageOwners.Contains("cake-contrib", StringComparer.OrdinalIgnoreCase);
					addin.AnalysisResult.XmlDocumentationAvailable = !string.IsNullOrEmpty(addin.XmlDocumentationFilePath);

					return addin;
				})
				.ToArray();
		}

		private IconAnalysisResult AnalyzeIcon(AddinMetadata addin, byte[] recommendedIcon, IEnumerable<KeyValuePair<AddinType, byte[]>> fancyIcons)
		{
			if (addin.EmbeddedIcon != null) return AnalyzeEmbeddedIcon(addin.EmbeddedIcon, addin.Type, recommendedIcon, fancyIcons);
			else if (addin.IconUrl != null) return AnalyzeLinkedIcon(addin.IconUrl);
			else return IconAnalysisResult.Unspecified;
		}

		private IconAnalysisResult AnalyzeEmbeddedIcon(byte[] embeddedIcon, AddinType addinType, byte[] recommendedIcon, IEnumerable<KeyValuePair<AddinType, byte[]>> fancyIcons)
		{
			// Check if the icon matches the recommended Cake-Contrib icon
			if (Misc.ByteArrayCompare(embeddedIcon, recommendedIcon))
			{
				return IconAnalysisResult.EmbeddedCakeContrib;
			}

			// Check if the icon matches one of the "fancy" icons of the appropriate type
			foreach (var fancyIcon in fancyIcons)
			{
				if (fancyIcon.Key.IsFlagSet(addinType) && Misc.ByteArrayCompare(embeddedIcon, fancyIcon.Value))
				{
					return IconAnalysisResult.EmbeddedFancyCakeContrib;
				}
			}

			// The icon doesn't match any of the recommended icons
			return IconAnalysisResult.EmbeddedCustom;
		}

		private IconAnalysisResult AnalyzeLinkedIcon(Uri iconUrl)
		{
			if (iconUrl.AbsoluteUri.EqualsIgnoreCase(Constants.OLD_CAKE_CONTRIB_ICON_URL)) return IconAnalysisResult.RawgitUrl;
			if (iconUrl.AbsoluteUri.EqualsIgnoreCase(Constants.NEW_CAKE_CONTRIB_ICON_URL)) return IconAnalysisResult.JsDelivrUrl;
			return IconAnalysisResult.CustomUrl;
		}
	}
}
