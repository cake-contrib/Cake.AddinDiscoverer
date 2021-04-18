using Cake.AddinDiscoverer.Models;
using Cake.AddinDiscoverer.Utilities;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Cake.AddinDiscoverer.Steps
{
	internal class GenerateExcelReportStep : IStep
	{
		public bool PreConditionIsMet(DiscoveryContext context) => context.Options.ExcelReportToFile || context.Options.ExcelReportToRepo;

		public string GetDescription(DiscoveryContext context) => "Generate the excel report";

		public async Task ExecuteAsync(DiscoveryContext context, TextWriter log)
		{
			ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

			using (var excel = new ExcelPackage(new FileInfo(context.ExcelReportPath)))
			{
				var deprecatedAddins = context.Addins.Where(addin => addin.IsDeprecated).ToArray();
				var auditedAddins = context.Addins.Where(addin => !addin.IsDeprecated && string.IsNullOrEmpty(addin.AnalysisResult.Notes)).ToArray();
				var exceptionAddins = context.Addins.Where(addin => !addin.IsDeprecated && !string.IsNullOrEmpty(addin.AnalysisResult.Notes)).ToArray();

				var namedStyle = excel.Workbook.Styles.CreateNamedStyle("HyperLink");
				namedStyle.Style.Font.UnderLine = true;
				namedStyle.Style.Font.Color.SetColor(Color.Blue);

				// One worksheet per version of Cake
				foreach (var cakeVersion in Constants.CAKE_VERSIONS.OrderByDescending(cakeVersion => cakeVersion.Version))
				{
					GenerateExcelWorksheet(auditedAddins, cakeVersion, AddinType.Addin | AddinType.Module, $"Cake {cakeVersion.Version}", excel);
				}

				// One worksheet for recipes
				GenerateExcelWorksheet(auditedAddins, null, AddinType.Recipe, "Recipes", excel);

				// Exceptions report
				GenerateExcelWorksheetWithNotes(exceptionAddins, "Exceptions", excel);

				// Deprecated report
				GenerateExcelWorksheetWithNotes(deprecatedAddins, "Deprecated", excel);

				// Save the Excel file
				excel.Save();
			}

			await Task.Delay(1).ConfigureAwait(false);
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
			worksheet.Cells[1, 2].Value = "Notes";

			var row = 1;
			foreach (var addin in addins.OrderBy(p => p.Name))
			{
				row++;
				worksheet.Cells[row, 1].Value = addin.Name;
				worksheet.Cells[row, 2].Value = addin.AnalysisResult.Notes?.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[0] ?? string.Empty;
			}

			// Resize columns and freeze the top row
			worksheet.Cells[1, 1, row, 2].AutoFitColumns();
			worksheet.View.FreezePanes(2, 1);
		}
	}
}
