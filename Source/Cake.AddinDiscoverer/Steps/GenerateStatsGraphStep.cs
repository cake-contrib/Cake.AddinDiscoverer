using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using CsvHelper;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
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

		public Task ExecuteAsync(DiscoveryContext context, TextWriter log, CancellationToken cancellationToken)
		{
			// ===========================================================================
			// STEP 1: Load data from the previously generated CSV file
			using TextReader reader = new StreamReader(context.StatsSaveLocation);
			var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
			csv.Context.TypeConverterOptionsCache.GetOptions<DateTime>().Formats = new[] { Constants.CSV_DATE_FORMAT };

			var auditedCakeVersions = Constants.CAKE_VERSIONS.Select(c => c.Version.ToString(3));
			var csvRecords = csv.GetRecords<AddinProgressSummary>().Where(r => auditedCakeVersions.Contains(r.CakeVersion)).ToList();
			var recordsGroupedByCakeVersion = csvRecords.GroupBy(r => r.CakeVersion).ToList();

			// ===========================================================================
			// STEP 2: Prepare the graph
			var graphPath = Path.Combine(context.TempFolder, "Audit_progress.png");

			var plotModel = new PlotModel
			{
				Title = "Addins compatibility over time",
				Subtitle = "Percentage of addins compatible with a given version of Cake",
				Background = OxyColors.White
			};

			plotModel.Legends.Add(new Legend()
			{
				LegendTitle = "Cake Version",
				LegendPosition = LegendPosition.TopLeft,
			});

			var startTime = csvRecords.Min(r => r.Date);
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

			// ===========================================================================
			// STEP 3: add data to the graph
			foreach (var grp in recordsGroupedByCakeVersion)
			{
				var series = new LineSeries()
				{
					Title = grp.Key
				};
				foreach (var statsSummary in grp)
				{
					series.Points.Add(new DataPoint(DateTimeAxis.ToDouble(statsSummary.Date), (Convert.ToDouble(statsSummary.CompatibleCount) / Convert.ToDouble(statsSummary.TotalCount)) * 100));
				}

				plotModel.Series.Add(series);
			}

			// ===========================================================================
			// STEP 4: save graph to PNG file
			var pngExporter = new PngExporter { Width = 600, Height = 400 };
			pngExporter.ExportToFile(plotModel, graphPath);

			return Task.CompletedTask;
		}
	}
}
