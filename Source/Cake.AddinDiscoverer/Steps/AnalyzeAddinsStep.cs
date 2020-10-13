using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class AnalyzeAddinsStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => true;

		public string GetDescription(DiscoveryContext context) => "Analyze addins";

		public async Task ExecuteAsync(DiscoveryContext context)
		{
			HttpClient client = new HttpClient();
			byte[] recommendedCakeContribIcon = await client.GetByteArrayAsync(Constants.NEW_CAKE_CONTRIB_ICON_URL).ConfigureAwait(false);

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

					if (addin.EmbeddedIcon != null)
					{
						if (Misc.ByteArrayCompare(addin.EmbeddedIcon, recommendedCakeContribIcon))
						{
							addin.AnalysisResult.Icon = IconAnalysisResult.EmbeddedCakeContrib;
						}
						else
						{
							addin.AnalysisResult.Icon = IconAnalysisResult.EmbeddedCustom;
						}
					}
					else if (addin.IconUrl != null)
					{
						if (addin.IconUrl.AbsoluteUri.EqualsIgnoreCase(Constants.OLD_CAKE_CONTRIB_ICON_URL)) addin.AnalysisResult.Icon = IconAnalysisResult.RawgitUrl;
						else if (addin.IconUrl.AbsoluteUri.EqualsIgnoreCase(Constants.NEW_CAKE_CONTRIB_ICON_URL)) addin.AnalysisResult.Icon = IconAnalysisResult.JsDelivrUrl;
						else addin.AnalysisResult.Icon = IconAnalysisResult.CustomUrl;
					}
					else
					{
						addin.AnalysisResult.Icon = IconAnalysisResult.Unspecified;
					}

					addin.AnalysisResult.TransferredToCakeContribOrganization = addin.RepositoryOwner?.Equals(Constants.CAKE_CONTRIB_REPO_OWNER, StringComparison.OrdinalIgnoreCase) ?? false;
					addin.AnalysisResult.ObsoleteLicenseUrlRemoved = !string.IsNullOrEmpty(addin.License);
					addin.AnalysisResult.RepositoryInfoProvided = addin.RepositoryUrl != null;
					addin.AnalysisResult.PackageCoOwnedByCakeContrib = addin.NuGetPackageOwners.Contains("cake-contrib", StringComparer.OrdinalIgnoreCase);

					return addin;
				})
				.ToArray();
		}
	}
}
