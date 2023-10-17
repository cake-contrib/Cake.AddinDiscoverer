using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using Cake.Incubator.StringExtensions;
using NuGet.Common;
using NuGet.Protocol.Core.Types;
using System;
using System.Collections.Generic;
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

			// Discover all the existing addins
			var packageNames = new List<string>();
			if (!string.IsNullOrEmpty(context.Options.AddinName))
			{
				packageNames.Add(context.Options.AddinName);
			}
			else
			{
				// Get all the packages matching the naming convention (regardless of the stable/prerelease status)
				await foreach (var packageMetadata in SearchForPackages(context.NugetRepository, "Cake", true, CancellationToken.None))
				{
					packageNames.Add(packageMetadata.Identity.Id);
				}

				// Add the "white listed" packages
				packageNames.AddRange(context.IncludedAddins);

				// Remove the "black listed" packages
				packageNames.RemoveAll(packageName => context.ExcludedAddins.Any(excludedAddinName => packageName.IsMatch(excludedAddinName)));
			}

			// Sort the package names alphabetically, for convenience
			packageNames.Sort();

			// Retrieve the metadata from each version of the package
			var metadata = await packageNames
				.Distinct()
				.ForEachAsync(
					async packageName =>
					{
						var packageMetadata = await FetchPackageMetadata(nugetPackageMetadataClient, packageName).ConfigureAwait(false);
						var addinMetadata = await ConvertPackageMetadataToAddinMetadataAsync(packageMetadata, packageName).ConfigureAwait(false);
						return addinMetadata;
					},
					Constants.MAX_NUGET_CONCURENCY)
				.ConfigureAwait(false);

			// Filter out the addins that were previously analysed
			var newAddins = metadata
				.SelectMany(item => item) // Flatten the array of arrays
				.Where(item => !context.Addins.Any(a => a.Name.EqualsIgnoreCase(item.Name) && a.NuGetPackageVersion == item.NuGetPackageVersion))
				.ToArray();

			// Add the new addins that have not yet been analysed
			context.Addins = context.Addins.Union(newAddins).ToArray();

			// Filter the addins if necessary
			if (!string.IsNullOrEmpty(context.Options.AddinName))
			{
				context.Addins = context.Addins.Where(addin => addin.Name.EqualsIgnoreCase(context.Options.AddinName)).ToArray();
			}

			// Sort the addins for convenience
			context.Addins = context.Addins
				.SortForAddinDisco()
				.ToArray();
		}

		private static Task<AddinMetadata[]> ConvertPackageMetadataToAddinMetadataAsync(IEnumerable<IPackageSearchMetadata> packageMetadata, string packageName)
		{
			return packageMetadata
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
							Analyzed = false,
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

							// It's important to use the same name for all versions of a given package rather than
							// rely on the package.Identity.Id because the casing of the Id could change from one
							// version to the next. I am aware of two cases where the Id of the package is not
							// constant between versions and in both cases the casing changed between versions.
							// Cake.Aws.ElasticBeanstalk:
							// - The Id was Cake.Aws.ElasticBeanstalk for version 1.0.0
							// - The Id was Cake.AWS.ElasticBeanstalk from version 1.1.0 until 1.3.1
							// - The Id was Cake.Aws.ElasticBeanstalk from version 1.3.2 until 1.3.3
							// - The Id is now Cake.AWS.ElasticBeanstalk
							// Cake.GitHubUtility:
							// - The Id was Cake.GithubUtility from version 0.1.0 until 0.3.0
							// - The Id is now Cake.GitHubUtility
							Name = packageName,

							NuGetPackageUrl = new Uri($"https://www.nuget.org/packages/{packageName}/"),
							NuGetPackageOwners = packageOwners,
							NuGetPackageVersion = package.Identity.Version,
							IsDeprecated = false,
							IsPrerelease = package.Identity.Version.IsPrerelease,
							HasPrereleaseDependencies = false,
							Tags = tags,
							Type = AddinType.Unknown,
							PublishedOn = package.Published.Value.ToUniversalTime(),
						};

						if (package.Title.Contains("[DEPRECATED]", StringComparison.OrdinalIgnoreCase))
						{
							addinMetadata.IsDeprecated = true;
							addinMetadata.AnalysisResult.Notes = package.Description;
						}
						else
						{
							var deprecationMetadata = await package.GetDeprecationMetadataAsync().ConfigureAwait(false);

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
					},
					Constants.MAX_GITHUB_CONCURENCY);
		}

		private static Task<IEnumerable<IPackageSearchMetadata>> FetchPackageMetadata(PackageMetadataResource nugetPackageMetadataClient, string name)
		{
			return nugetPackageMetadataClient.GetMetadataAsync(name, true, false, new SourceCacheContext(), NullLogger.Instance, CancellationToken.None);
		}

		private static async IAsyncEnumerable<IPackageSearchMetadata> SearchForPackages(SourceRepository nugetRepository, string searchTerm, bool includePrerelease, [EnumeratorCancellation] CancellationToken cancellationToken = default)
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

			var nugetSearchClient = await nugetRepository.GetResourceAsync<PackageSearchResource>().ConfigureAwait(false);

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
