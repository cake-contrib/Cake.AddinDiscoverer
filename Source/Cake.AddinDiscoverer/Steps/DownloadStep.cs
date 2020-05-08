using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using NuGet.Common;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class DownloadStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => true;

		public string GetDescription(DiscoveryContext context)
		{
			if (string.IsNullOrEmpty(context.Options.AddinName)) return "Download latest packages from NuGet";
			else return $"Download latest package for {context.Options.AddinName}";
		}

		public async Task ExecuteAsync(DiscoveryContext context)
		{
			var nugetPackageDownloadClient = context.NugetRepository.GetResource<DownloadResource>();
			await context.Addins
				.ForEachAsync(
					async package =>
					{
						var packageFileName = Path.Combine(context.PackagesFolder, $"{package.Name}.{package.NuGetPackageVersion}.nupkg");
						if (!File.Exists(packageFileName))
						{
							// Delete prior versions of this package
							foreach (string f in Directory.EnumerateFiles(context.PackagesFolder, $"{package.Name}.*.nupkg"))
							{
								var expectedSplitLength = package.Name.Split('.').Length + package.NuGetPackageVersion.Split('.').Length;
								var fileName = Path.GetFileNameWithoutExtension(f);
								if (fileName.Split('.').Length == expectedSplitLength)
								{
									File.Delete(f);
								}
							}

							// Download the latest version of the package
							using var sourceCacheContext = new SourceCacheContext() { NoCache = true };
							var downloadContext = new PackageDownloadContext(sourceCacheContext, Path.GetTempPath(), true);
							var packageIdentity = new PackageIdentity(package.Name, new NuGet.Versioning.NuGetVersion(package.NuGetPackageVersion));

							using var result = await nugetPackageDownloadClient.GetDownloadResourceResultAsync(packageIdentity, downloadContext, string.Empty, NullLogger.Instance, CancellationToken.None);
							if (result.Status == DownloadResourceResultStatus.Cancelled)
							{
								throw new OperationCanceledException();
							}
							else if (result.Status == DownloadResourceResultStatus.NotFound)
							{
								throw new Exception(string.Format("Package '{0} {1}' not found", package.Name, package.NuGetPackageVersion));
							}
							else
							{
								await using var fileStream = File.OpenWrite(packageFileName);
								await result.PackageStream.CopyToAsync(fileStream);
							}
						}
					}, Constants.MAX_NUGET_CONCURENCY)
				.ConfigureAwait(false);
		}
	}
}
