using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using NuGet.Common;
using NuGet.Protocol.Core.Types;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class DiscoveryStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => true;

		public string GetDescription(DiscoveryContext context)
		{
			if (string.IsNullOrEmpty(context.Options.AddinName)) return "Search NuGet for all packages matching 'Cake.*'";
			else return $"Search NuGet for {context.Options.AddinName}";
		}

		public async Task ExecuteAsync(DiscoveryContext context, TextWriter log, CancellationToken cancellationToken)
		{
			var nugetPackageMetadataClient = await context.NugetRepository.GetResourceAsync<PackageMetadataResource>().ConfigureAwait(false);
			var addinPackages = new List<IPackageSearchMetadata>();

			//--------------------------------------------------
			// Get the metadata from NuGet.org
			if (!string.IsNullOrEmpty(context.Options.AddinName))
			{
				// Get metadata for one specific package
				var packageMetadata = await FetchPackageMetadata(nugetPackageMetadataClient, context.Options.AddinName).ConfigureAwait(false);
				if (packageMetadata != null)
				{
					addinPackages.AddRange(packageMetadata);
				}
			}
			else
			{
				// Get the most recent "stable" version of packages matching the naming convention.
				await foreach (var packageMetadata in SearchForPackages(context.NugetRepository, "Cake", false, CancellationToken.None))
				{
					addinPackages.Add(packageMetadata);
				}

				// Get the most recent version of packages matching the naming convention (regardless of the stable/prerelease status)
				await foreach (var packageMetadata in SearchForPackages(context.NugetRepository, "Cake", true, CancellationToken.None))
				{
					addinPackages.Add(packageMetadata);
				}

				// Get metadata for the packages we specifically want to include
				foreach (var additionalPackageName in context.IncludedAddins)
				{
					var packageMetadata = await FetchPackageMetadata(nugetPackageMetadataClient, additionalPackageName).ConfigureAwait(false);
					if (packageMetadata != null)
					{
						addinPackages.AddRange(packageMetadata);
					}
				}
			}

			//--------------------------------------------------
			// Select the most recent "stable" release of each addin.
			// If an addin does not have any stable release, select the most recent, regardless of its stable/prerelease status
			var uniqueAddinPackages = addinPackages
				.GroupBy(p => p.Identity.Id)
				.Select(g => g
					.OrderBy(p => p.Identity.Version.IsPrerelease ? 1 : 0) // Stable versions are sorted first, prerelease versions sorted second
					.ThenByDescending(p => p.Published)
					.First())
				.ToArray();

			//--------------------------------------------------
			// Convert metadata from nuget into our own metadata
			context.Addins = await uniqueAddinPackages
				.ForEachAsync(
					async package =>
					{
						// As of June 2019, the 'Owners' metadata value returned from NuGet is always null.
						// This code is just in case they add this information to the metadata and don't let us know.
						// See feature request: https://github.com/NuGet/NuGetGallery/issues/5647
						var packageOwners = package.Owners?
							.Split(',', StringSplitOptions.RemoveEmptyEntries)
							.Select(owner => owner.Trim())
							.ToArray() ?? Array.Empty<string>();

						var tags = package.Tags?
							.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
							.Select(tag => tag.Trim())
							.ToArray() ?? Array.Empty<string>();

						var addinMetadata = new AddinMetadata()
						{
							AnalysisResult = new AddinAnalysisResult()
							{
								CakeRecipeIsUsed = false,
								CakeRecipeVersion = null,
								CakeRecipeIsPrerelease = false,
								CakeRecipeIsLatest = false
							},
							Maintainer = package.Authors,
							Description = package.Description,
							ProjectUrl = package.ProjectUrl,
							IconUrl = package.IconUrl,
							Name = package.Identity.Id,
							NuGetPackageUrl = new Uri($"https://www.nuget.org/packages/{package.Identity.Id}/"),
							NuGetPackageOwners = packageOwners,
							NuGetPackageVersion = package.Identity.Version.ToNormalizedString(),
							IsDeprecated = false,
							IsPrerelease = package.Identity.Version.IsPrerelease,
							HasPrereleaseDependencies = false,
							Tags = tags,
							Type = AddinType.Unknown,
							PublishedOn = Constants.UtcMinDateTime,
							RepoContent = ImmutableDictionary<string, Stream>.Empty
						};

						if (package.Title.Contains("[DEPRECATED]", StringComparison.OrdinalIgnoreCase))
						{
							addinMetadata.IsDeprecated = true;
							addinMetadata.AnalysisResult.Notes = package.Description;
						}
						else
						{
							// The metadata returned by nugetSearchClient.SearchAsync is minimal.
							// That's why we need to invoke nugetPackageMetadataClient.GetMetadataAsync to get more detailed metadata.
							var packageMetadata = await nugetPackageMetadataClient.GetMetadataAsync(package.Identity.Id, true, false, new SourceCacheContext(), NullLogger.Instance, CancellationToken.None).ConfigureAwait(false);
							var detailedPackageMetadata = packageMetadata.SingleOrDefault(m => m.Identity.Equals(package.Identity));

							if (detailedPackageMetadata != null)
							{
								addinMetadata.PublishedOn = detailedPackageMetadata.Published.Value;
							}

							// We need to look at the most recent version (even if that's not the version that would otherwise be analyzed)
							// to determine if the package has been deprecated
							var mostRecentPackageMetadata = packageMetadata.OrderByDescending(p => p.Published).FirstOrDefault();
							var deprecationMetadata = mostRecentPackageMetadata != null ? await mostRecentPackageMetadata.GetDeprecationMetadataAsync().ConfigureAwait(false) : null;

							if (deprecationMetadata != null)
							{
								// Derive a message based on the 'Reasons' enumeration in case an actual message has not been provided
								var deprecationReasonMessage = default(string);
								if (deprecationMetadata.Reasons == null || !deprecationMetadata.Reasons.Any())
								{
									deprecationReasonMessage = "This package has been deprecated but the author has not provided a reason.";
								}
								else if (deprecationMetadata.Reasons.Count() == 1)
								{
									deprecationReasonMessage = "This package has been deprecated for the following reason: " + deprecationMetadata.Reasons.First();
								}
								else
								{
									deprecationReasonMessage = "This package has been deprecated for the following reasons: " + string.Join(", ", deprecationMetadata.Reasons);
								}

								addinMetadata.IsDeprecated = true;
								addinMetadata.AnalysisResult.Notes = deprecationMetadata.Message ?? deprecationReasonMessage;
							}
						}

						return addinMetadata;
					}, Constants.MAX_NUGET_CONCURENCY)
				.ConfigureAwait(false);
		}

		private async Task<IEnumerable<IPackageSearchMetadata>> FetchPackageMetadata(PackageMetadataResource nugetPackageMetadataClient, string name)
		{
			var searchMetadata = await nugetPackageMetadataClient.GetMetadataAsync(name, true, false, new SourceCacheContext(), NullLogger.Instance, CancellationToken.None).ConfigureAwait(false);
			return searchMetadata;
		}

		private async IAsyncEnumerable<IPackageSearchMetadata> SearchForPackages(SourceRepository nugetRepository, string searchTerm, bool includePrerelease, [EnumeratorCancellation] CancellationToken cancellationToken = default)
		{
			// The max value allowed by the NuGet search API is 1000.
			// This large value is important to ensure results fit in a single page therefore avoiding the problem with duplicates.
			// For more details, see: https://github.com/NuGet/NuGetGallery/issues/7494
			const int take = 1000;

			var skip = 0;
			var filters = new SearchFilter(includePrerelease)
			{
				IncludeDelisted = false,
				OrderBy = SearchOrderBy.Id
			};

			var nugetSearchClient = nugetRepository.GetResource<PackageSearchResource>();

			while (true)
			{
				var searchResult = await nugetSearchClient.SearchAsync(searchTerm, filters, skip, take, NullLogger.Instance, cancellationToken).ConfigureAwait(false);
				skip += take;

				if (!searchResult.Any())
				{
					break;
				}

				foreach (var packageMetadata in searchResult)
				{
					if (packageMetadata.Identity.Id.StartsWith($"{searchTerm}.", StringComparison.OrdinalIgnoreCase))
					{
						yield return packageMetadata;
					}
				}
			}
		}
	}
}
