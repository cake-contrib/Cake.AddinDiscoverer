using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using CsvHelper;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class GenerateStatsGraphStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context)
		{
			// Do not update the stats if we are only auditing a single addin.
			return string.IsNullOrEmpty(context.Options.AddinName);
		}

		public string GetDescription(DiscoveryContext context) => "Generate the graph showing addin compatibility over time";

		public async Task ExecuteAsync(DiscoveryContext context, TextWriter log)
		{
			var graphPath = Path.Combine(context.TempFolder, "Audit_progress.png");

			var plotModel = new PlotModel
			{
				Title = "Addins compatibility over time",
				Subtitle = "Percentage of all known addins compatible with a given version of Cake"
			};
			var startTime = new DateTime(2018, 3, 21, 0, 0, 0, DateTimeKind.Utc); // We started auditing addins on March 22 2018
			var minDate = DateTimeAxis.ToDouble(startTime);
			var maxDate = minDate + (DateTime.UtcNow - startTime).TotalDays + 2;

			plotModel.Axes.Add(new DateTimeAxis
			{
				Position = AxisPosition.Bottom,
				Minimum = minDate,
				Maximum = maxDate,
				IntervalType = DateTimeIntervalType.Months,
				Title = "Date",
				StringFormat = "yyyy-MM-dd"
			});
			plotModel.Axes.Add(new LinearAxis
			{
				Position = AxisPosition.Left,
				Minimum = 0,
				Maximum = 100,
				MajorStep = 25,
				MinorStep = 5,
				MajorGridlineStyle = LineStyle.Solid,
				MinorGridlineStyle = LineStyle.Dot,
				Title = "Percent"
			});

			using (TextReader reader = new StreamReader(context.StatsSaveLocation))
			{
				var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
				csv.Configuration.TypeConverterCache.AddConverter<DateTime>(new DateConverter("yyyy-MM-dd HH:mm:ss"));

				var recordsGroupedByCakeVersion = csv
					.GetRecords<AddinProgressSummary>()
					.GroupBy(r => r.CakeVersion);

				foreach (var grp in recordsGroupedByCakeVersion)
				{
					var series = new LineSeries()
					{
						Title = $"Cake {grp.Key}"
					};
					foreach (var statsSummary in grp)
					{
						series.Points.Add(new DataPoint(DateTimeAxis.ToDouble(statsSummary.Date), (Convert.ToDouble(statsSummary.CompatibleCount) / Convert.ToDouble(statsSummary.TotalCount)) * 100));
					}

					plotModel.Series.Add(series);
				}
			}

			var pngExporter = new PngExporter { Width = 600, Height = 400, Background = OxyColors.White };
			pngExporter.ExportToFile(plotModel, graphPath);

			await Task.Delay(1).ConfigureAwait(false);
		}
	}
}
