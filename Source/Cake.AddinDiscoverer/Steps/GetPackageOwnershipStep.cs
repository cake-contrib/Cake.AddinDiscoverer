using Cake.AddinDiscoverer.Models;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
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
		public bool PreConditionIsMet(DiscoveryContext context) => true;

		public string GetDescription(DiscoveryContext context) => "Get NuGet package owners";

		public async Task ExecuteAsync(DiscoveryContext context, TextWriter log)
		{
			const string uri = "https://nugetprod0.blob.core.windows.net/ng-search-data/owners.json";

			var ownersFileJsonContent = await context.HttpClient.GetStringAsync(uri).ConfigureAwait(false);
			var packageOwners = JArray.Parse(ownersFileJsonContent)
				.ToDictionary(
					e => e[0].Value<string>(),
					e => e[1].Values<string>().ToArray()
				);

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
		}
	}
}
