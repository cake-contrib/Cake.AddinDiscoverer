using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	/// <summary>
	/// This is necesary because NuGet does not include package owners in the metadata we retrieve in DiscoveryStep.
	/// The metadata returned from the NuGet API has a 'Owners' property but it always contains null.
	/// This step will be obsolete when the NuGet API is improved to include the owners.
	/// </summary>
	internal class GetPackageOwnershipStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => !context.Options.ExcludeSlowSteps;

		public string GetDescription(DiscoveryContext context)
		{
			if (string.IsNullOrEmpty(context.Options.AddinName)) return "Get NuGet package owners for all packages matching 'Cake.*'";
			else return $"Get NuGet package owners for {context.Options.AddinName}";
		}

		public async Task ExecuteAsync(DiscoveryContext context)
		{
			const string uri = "https://nugetprod0.blob.core.windows.net/ng-search-data/owners.json";

			IDictionary<string, string[]> packageOwners;

			using (var webClient = new WebClient())
			{
				var ownersFileJsonContent = webClient.DownloadString(new Uri(uri));
				packageOwners = JArray.Parse(ownersFileJsonContent)
					.ToDictionary(e => e[0].Value<string>(), e => e[1].Values<string>().ToArray());
			}

			context.Addins = context.Addins
				.Select(addin =>
				{
					if (addin.NuGetPackageOwners == Array.Empty<string>())
					{
						if (packageOwners.TryGetValue(addin.Name, out string[] owners))
						{
							addin.NuGetPackageOwners = owners;
						}
					}

					return addin;
				})
				.ToArray();

			await Task.Delay(1).ConfigureAwait(false);
		}
	}
}
