using Cake.AddinDiscoverer.Models;
using NuGet.Packaging;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class LoadStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => true;

		public string GetDescription(DiscoveryContext context) => "Load packages into memory";

		public Task ExecuteAsync(DiscoveryContext context, TextWriter log, CancellationToken cancellationToken)
		{
			context.Addins = context.Addins
				.Select(addin =>
				{
					try
					{
						var packageFileName = Path.Combine(context.PackagesFolder, $"{addin.Name}.{addin.NuGetPackageVersion}.nupkg");
						if (File.Exists(packageFileName))
						{
							addin.NuGetPackage = new PackageArchiveReader(File.Open(packageFileName, System.IO.FileMode.Open, FileAccess.Read, FileShare.Read));
						}

						var symbolsFileName = Path.Combine(context.PackagesFolder, $"{addin.Name}.{addin.NuGetPackageVersion}.snupkg");
						if (File.Exists(symbolsFileName))
						{
							addin.SymbolsPackage = new PackageArchiveReader(File.Open(symbolsFileName, System.IO.FileMode.Open, FileAccess.Read, FileShare.Read));
						}
					}
					catch (Exception e)
					{
						addin.AnalysisResult.Notes += $"Load: {e.GetBaseException().Message}{Environment.NewLine}";
					}

					return addin;
				})
				.ToArray();

			return Task.CompletedTask;
		}
	}
}
