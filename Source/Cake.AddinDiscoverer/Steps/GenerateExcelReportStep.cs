using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static Cake.AddinDiscoverer.Models.ReportData;

namespace Cake.AddinDiscoverer.Steps
{
	internal class GenerateExcelReportStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => context.Options.GenerateExcelReport;

		public string GetDescription(DiscoveryContext context) => "Generate the excel report";

		public async Task ExecuteAsync(DiscoveryContext context, TextWriter log, CancellationToken cancellationToken)
		{
			ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

			var cakeVersionsForReport = Constants.CAKE_VERSIONS.Where(cakeVersion => cakeVersion.Version != Constants.VERSION_ZERO).ToArray();
			var latestCakeVersion = cakeVersionsForReport.Max();

			var reportData = new ReportData(context.Addins);
			var mostRecentAddins = reportData.GetAddinsForCakeVersion(latestCakeVersion, CakeVersionComparison.LessThanOrEqual);
			var analyzedAddins = mostRecentAddins.Where(a => !a.IsDeprecated && string.IsNullOrEmpty(a.AnalysisResult.Notes));
			var exceptionAddins = mostRecentAddins.Where(a => !a.IsDeprecated && !string.IsNullOrEmpty(a.AnalysisResult.Notes));
			var deprecatedAddins = mostRecentAddins.Where(a => a.IsDeprecated);

			using (var excel = new ExcelPackage(new FileInfo(context.ExcelReportPath)))
			{
				var namedStyle = excel.Workbook.Styles.CreateNamedStyle("HyperLink");
				namedStyle.Style.Font.UnderLine = true;
				namedStyle.Style.Font.Color.SetColor(Color.Blue);

				// One worksheet per version of Cake (reverse order so first tab in excel shows data for most recent version of Cake)
				foreach (var cakeVersion in cakeVersionsForReport.OrderByDescending(v => v.Version))
				{
					var addins = reportData.GetAddinsForCakeVersion(cakeVersion, CakeVersionComparison.LessThanOrEqual)
						.Where(addin => !addin.IsDeprecated)
						.Where(addin => string.IsNullOrEmpty(addin.AnalysisResult.Notes))
						.ToArray();

					GenerateExcelWorksheet(addins, cakeVersion, AddinType.Addin | AddinType.Module, $"Cake {cakeVersion.Version}", excel);
				}

				// One worksheet for recipes
				GenerateExcelWorksheet(analyzedAddins, latestCakeVersion, AddinType.Recipe, "Recipes", excel);

				// Exceptions report
				GenerateExcelWorksheetWithNotes(exceptionAddins, "Exceptions", excel);

				// Deprecated report
				GenerateExcelWorksheetWithNotes(deprecatedAddins, "Deprecated", excel);

				// XML documentation report
				GenerateExcelWorksheetWithXmlDocumentationNotes(analyzedAddins, "XML documentation", excel);

				// Save the Excel file
				await excel.SaveAsync(cancellationToken).ConfigureAwait(false);
			}
		}

		private void GenerateExcelWorksheet(IEnumerable<AddinMetadata> addins, CakeVersion cakeVersion, AddinType type, string caption, ExcelPackage excel)
		{
			var filteredAddins = addins
				.Where(addin => addin.Type.IsFlagSet(type))
				.ToArray();

			var reportColumns = Constants.REPORT_COLUMNS
				.Where(column => column.Destination.HasFlag(DataDestination.Excel))
				.Where(column => column.ApplicableTo.HasFlag(type))
				.Select((data, index) => new { Index = index, Data = data })
				.ToArray();

			// Create the worksheet
			var worksheet = excel.Workbook.Worksheets.Add(caption);

			// Header row
			foreach (var column in reportColumns)
			{
				worksheet.Cells[1, column.Index + 1].Value = column.Data.Header;
			}

			// One row per addin
			var row = 1;
			foreach (var addin in filteredAddins.OrderBy(a => a.Name))
			{
				row++;

				foreach (var column in reportColumns)
				{
					if (column.Data.ApplicableTo.HasFlag(addin.Type))
					{
						var cell = worksheet.Cells[row, column.Index + 1];
						cell.Value = column.Data.GetContent(addin);

						var color = column.Data.GetCellColor(addin, cakeVersion);
						if (color != Color.Empty)
						{
							cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
							cell.Style.Fill.BackgroundColor.SetColor(color);
						}

						var hyperlink = column.Data.GetHyperLink(addin);
						if (hyperlink != null)
						{
							cell.Hyperlink = hyperlink;
							cell.StyleName = "HyperLink";
						}
					}
				}
			}

			// Freeze the top row and first column
			worksheet.View.FreezePanes(2, 2);

			// Setup auto-filter
			worksheet.Cells[1, 1, 1, reportColumns.Length].AutoFilter = true;

			// Format the worksheet
			worksheet.Row(1).Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
			if (filteredAddins.Any())
			{
				foreach (var column in reportColumns)
				{
					worksheet.Cells[2, column.Index + 1, row, column.Index + 1].Style.HorizontalAlignment = column.Data.Align;
				}
			}

			// Resize columns
			worksheet.Cells[1, 1, row, reportColumns.Length].AutoFitColumns();

			// Make columns a little bit wider to account for the filter "drop-down arrow" button
			foreach (var column in reportColumns)
			{
				worksheet.Column(column.Index + 1).Width += 2.14;
			}
		}

		private void GenerateExcelWorksheetWithNotes(IEnumerable<AddinMetadata> addins, string caption, ExcelPackage excel)
		{
			var worksheet = excel.Workbook.Worksheets.Add(caption);

			worksheet.Cells[1, 1].Value = "Addin";
			worksheet.Cells[1, 2].Value = "Version";
			worksheet.Cells[1, 3].Value = "Notes";

			var row = 1;
			foreach (var addin in addins.OrderBy(p => p.Name))
			{
				row++;
				worksheet.Cells[row, 1].Value = addin.Name;
				worksheet.Cells[row, 2].Value = addin.NuGetPackageVersion;
				worksheet.Cells[row, 3].Value = addin.AnalysisResult.Notes?.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[0] ?? string.Empty;
			}

			// Resize columns and freeze the top row
			worksheet.Cells[1, 1, row, 3].AutoFitColumns();
			worksheet.View.FreezePanes(2, 1);
		}

		private void GenerateExcelWorksheetWithXmlDocumentationNotes(IEnumerable<AddinMetadata> addins, string caption, ExcelPackage excel)
		{
			var worksheet = excel.Workbook.Worksheets.Add(caption);

			worksheet.Cells[1, 1].Value = "Addin";
			worksheet.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

			worksheet.Cells[1, 2].Value = "Issue";
			worksheet.Cells[1, 2].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

			var row = 1;
			foreach (var addin in addins.OrderBy(p => p.Name))
			{
				foreach (var note in addin.AnalysisResult.XmlDocumentationAnalysisNotes)
				{
					row++;
					worksheet.Cells[row, 1].Value = addin.Name;
					worksheet.Cells[row, 2].Value = note ?? string.Empty;
				}
			}

			// Freeze the top row and first column
			worksheet.View.FreezePanes(2, 2);

			// Setup auto-filter
			worksheet.Cells[1, 1, 1, 2].AutoFilter = true;

			// Resize columns
			worksheet.Cells[1, 1, row, 2].AutoFitColumns();

			// Make columns a little bit wider to account for the filter "drop-down arrow" button
			for (int columnIndex = 1; columnIndex <= 2; columnIndex++)
			{
				worksheet.Column(columnIndex).Width += 2.14;
			}
		}
	}
}
