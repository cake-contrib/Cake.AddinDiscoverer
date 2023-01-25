using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using NuGet.Common;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class DownloadStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => true;

		public string GetDescription(DiscoveryContext context)
		{
			if (string.IsNullOrEmpty(context.Options.AddinName)) return "Download packages from NuGet";
			return $"Download package for {context.Options.AddinName}";
		}

		public async Task ExecuteAsync(DiscoveryContext context, TextWriter log, CancellationToken cancellationToken)
		{
			var nugetPackageDownloadClient = context.NugetRepository.GetResource<DownloadResource>();

			await context.Addins
				.Where(addin => !addin.Analyzed) // Only process addins that were not previously analized
				.ForEachAsync(
					async package =>
					{
						await DownloadNugetPackage(nugetPackageDownloadClient, context, package).ConfigureAwait(false);
						await DownloadSymbolsPackage(context, package).ConfigureAwait(false);
					}, Constants.MAX_NUGET_CONCURENCY)
				.ConfigureAwait(false);
		}

		private async Task DownloadNugetPackage(DownloadResource nugetClient, DiscoveryContext context, AddinMetadata package)
		{
			var packageFileName = Path.Combine(context.PackagesFolder, $"{package.Name}.{package.NuGetPackageVersion}.nupkg");
			if (!File.Exists(packageFileName))
			{
				// Download the package
				using var sourceCacheContext = new SourceCacheContext() { NoCache = true };
				var downloadContext = new PackageDownloadContext(sourceCacheContext, Path.GetTempPath(), true);
				var packageIdentity = new PackageIdentity(package.Name, new NuGet.Versioning.NuGetVersion(package.NuGetPackageVersion));

				using var result = await nugetClient.GetDownloadResourceResultAsync(packageIdentity, downloadContext, string.Empty, NullLogger.Instance, CancellationToken.None).ConfigureAwait(false);
				switch (result.Status)
				{
					case DownloadResourceResultStatus.Cancelled:
						throw new OperationCanceledException();
					case DownloadResourceResultStatus.NotFound:
						throw new Exception($"Package '{package.Name} {package.NuGetPackageVersion}' not found");
					default:
						{
							await using var fileStream = File.OpenWrite(packageFileName);
							await result.PackageStream.CopyToAsync(fileStream).ConfigureAwait(false);
							break;
						}
				}
			}
		}

		private async Task DownloadSymbolsPackage(DiscoveryContext context, AddinMetadata package)
		{
			var packageFileName = Path.Combine(context.PackagesFolder, $"{package.Name}.{package.NuGetPackageVersion}.snupkg");
			if (!File.Exists(packageFileName))
			{
				// Download the symbols package
				try
				{
					var response = await context.HttpClient
						.GetAsync($"https://www.nuget.org/api/v2/symbolpackage/{package.Name}/{package.NuGetPackageVersion}")
						.ConfigureAwait(false);

					if (response.IsSuccessStatusCode)
					{
						await using var getStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
						await using var fileStream = File.OpenWrite(packageFileName);
						await getStream.CopyToAsync(fileStream).ConfigureAwait(false);
					}
				}
				catch (Exception e)
				{
					throw new Exception($"An error occured while attempting to download symbol package for '{package.Name} {package.NuGetPackageVersion}'", e);
				}
			}
		}
	}
}
