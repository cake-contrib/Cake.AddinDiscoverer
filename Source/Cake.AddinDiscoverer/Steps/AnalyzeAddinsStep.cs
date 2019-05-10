using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class AnalyzeAddinsStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => true;

		public string GetDescription(DiscoveryContext context) => "Analyze addins";

		public async Task ExecuteAsync(DiscoveryContext context)
		{
			context.Addins = context.Addins
				.Select(addin =>
				{
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

					addin.AnalysisResult.UsingNewCakeContribIcon = addin.IconUrl?.AbsoluteUri.EqualsIgnoreCase(Constants.NEW_CAKE_CONTRIB_ICON_URL) ?? false;
					addin.AnalysisResult.UsingOldCakeContribIcon = addin.IconUrl?.AbsoluteUri.EqualsIgnoreCase(Constants.OLD_CAKE_CONTRIB_ICON_URL) ?? false;
					addin.AnalysisResult.TransferedToCakeContribOrganisation = addin.RepositoryOwner?.Equals(Constants.CAKE_CONTRIB_REPO_OWNER, StringComparison.OrdinalIgnoreCase) ?? false;
					addin.AnalysisResult.ObsoleteLicenseUrlRemoved = !string.IsNullOrEmpty(addin.License);
					addin.AnalysisResult.RepositoryInfoProvided = addin.RepositoryUrl != null;

					return addin;
				})
				.ToArray();

			await Task.Delay(1).ConfigureAwait(false);
		}
	}
}
