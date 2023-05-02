using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using CsvHelper;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class UpdateStatsCsvStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context)
		{
			// Do not update the stats if we are only auditing a single addin.
			return string.IsNullOrEmpty(context.Options.AddinName);
		}

		public string GetDescription(DiscoveryContext context) => "Update statistics";

		public async Task ExecuteAsync(DiscoveryContext context, TextWriter log, CancellationToken cancellationToken)
		{
			var content = await context.GithubClient.Repository.Content.GetAllContents(Constants.CAKE_CONTRIB_REPO_OWNER, Constants.CAKE_CONTRIB_REPO_NAME, Path.GetFileName(context.StatsSaveLocation)).ConfigureAwait(false);
			File.WriteAllText(context.StatsSaveLocation, content[0].Content);

			var cakeVersionsForReport = Constants.CAKE_VERSIONS.Where(cakeVersion => cakeVersion.Version != Constants.VERSION_ZERO).ToArray();

			using (var fs = new FileStream(context.StatsSaveLocation, FileMode.Append, FileAccess.Write))
			using (TextWriter writer = new StreamWriter(fs))
			{
				var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
				csv.Context.TypeConverterOptionsCache.GetOptions<DateTime>().Formats = new[] { Constants.CSV_DATE_FORMAT };

				var reportData = new ReportData(context.Addins);

				foreach (var cakeVersion in cakeVersionsForReport)
				{
					var addins = reportData
						.GetAddinsForCakeVersion(cakeVersion)
						.Where(a => a.Type.IsFlagSet(AddinType.Addin | AddinType.Module))
						.Where(a => !a.IsDeprecated && string.IsNullOrEmpty(a.AnalysisResult.Notes));

					var summary = new AddinProgressSummary
					{
						CakeVersion = cakeVersion.Version.ToString(),
						Date = DateTime.UtcNow,
						CompatibleCount = addins.Count(addin =>
						{
							return addin.AnalysisResult.CakeCoreVersion.IsUpToDate(cakeVersion.Version) &&
								addin.AnalysisResult.CakeCommonVersion.IsUpToDate(cakeVersion.Version);
						}),
						TotalCount = addins.Count()
					};

					csv.WriteRecord(summary);
					csv.NextRecord();
				}
			}
		}
	}
}
