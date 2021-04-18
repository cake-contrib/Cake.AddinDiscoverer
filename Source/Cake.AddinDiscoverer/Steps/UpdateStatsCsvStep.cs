using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using CsvHelper;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
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

		public async Task ExecuteAsync(DiscoveryContext context, TextWriter log)
		{
			var content = await context.GithubClient.Repository.Content.GetAllContents(Constants.CAKE_CONTRIB_REPO_OWNER, Constants.CAKE_CONTRIB_REPO_NAME, System.IO.Path.GetFileName(context.StatsSaveLocation)).ConfigureAwait(false);
			File.WriteAllText(context.StatsSaveLocation, content[0].Content);

			using (var fs = new FileStream(context.StatsSaveLocation, System.IO.FileMode.Append, FileAccess.Write))
			using (TextWriter writer = new StreamWriter(fs))
			{
				var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
				csv.Configuration.TypeConverterCache.AddConverter<DateTime>(new DateConverter(Constants.CSV_DATE_FORMAT));

				var addins = context.Addins.Where(addin => addin.Type == AddinType.Addin || addin.Type == AddinType.Module).ToArray();
				var validAddins = addins.Where(addin => !addin.IsDeprecated).ToArray();
				var auditedAddins = validAddins.Where(addin => string.IsNullOrEmpty(addin.AnalysisResult.Notes)).ToArray();
				var exceptionAddins = validAddins.Where(addin => !string.IsNullOrEmpty(addin.AnalysisResult.Notes)).ToArray();

				foreach (var cakeVersion in Constants.CAKE_VERSIONS)
				{
					var summary = new AddinProgressSummary
					{
						CakeVersion = cakeVersion.Version.ToString(),
						Date = DateTime.UtcNow,
						CompatibleCount = auditedAddins.Count(addin =>
						{
							return addin.AnalysisResult.CakeCoreVersion.IsUpToDate(cakeVersion.Version) &&
								addin.AnalysisResult.CakeCommonVersion.IsUpToDate(cakeVersion.Version);
						}),
						TotalCount = auditedAddins.Count() + exceptionAddins.Count()
					};

					csv.WriteRecord(summary);
					csv.NextRecord();
				}
			}
		}
	}
}
