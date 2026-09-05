using ClosedXML.Excel;

namespace AnythingLLMReviewTranslator;

public static class ExcelExportService
{
    public static void ExportReviewRows(string filePath, IReadOnlyList<ReviewRowViewModel> rows)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path is required.", nameof(filePath));
        }

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Sheet1");

        var headers = new[]
        {
            "Id",
            "AllText",
            "MsgCtxt",
            "MsgId",
            "MsgStr",
            "SuggestedTranslation",
            "Rating",
            "SourceFilePath",
            "ImportedAtUtc",
            "TranslationLocked"
        };

        for (var i = 0; i < headers.Length; i++)
        {
            var cell = worksheet.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var excelRow = index + 2;

            worksheet.Cell(excelRow, 1).Value = row.Id;
            worksheet.Cell(excelRow, 2).Value = row.AllText ?? string.Empty;
            worksheet.Cell(excelRow, 3).Value = row.MsgCtxt ?? string.Empty;
            worksheet.Cell(excelRow, 4).Value = row.MsgId ?? string.Empty;
            worksheet.Cell(excelRow, 5).Value = row.MsgStr ?? string.Empty;
            worksheet.Cell(excelRow, 6).Value = row.SuggestedTranslation ?? string.Empty;
            worksheet.Cell(excelRow, 7).Value = row.Rating.HasValue ? (double)row.Rating.Value : string.Empty;
            worksheet.Cell(excelRow, 8).Value = row.SourceFilePath ?? string.Empty;
            worksheet.Cell(excelRow, 9).Value = row.ImportedAtUtc;
            worksheet.Cell(excelRow, 10).Value = row.TranslationLocked.HasValue ? (row.TranslationLocked.Value ? 1 : 0) : string.Empty;
        }

        worksheet.SheetView.FreezeRows(1);
        worksheet.RangeUsed()?.SetAutoFilter();
        worksheet.Columns().AdjustToContents();
        worksheet.Column(2).Width = Math.Min(Math.Max(worksheet.Column(2).Width, 40), 80);
        worksheet.Column(6).Width = Math.Min(Math.Max(worksheet.Column(6).Width, 40), 80);
        worksheet.Column(8).Width = Math.Min(Math.Max(worksheet.Column(8).Width, 30), 70);
        worksheet.Column(9).Width = 22;

        worksheet.Column(2).Style.Alignment.WrapText = true;
        worksheet.Column(3).Style.Alignment.WrapText = true;
        worksheet.Column(5).Style.Alignment.WrapText = true;
        worksheet.Column(6).Style.Alignment.WrapText = true;
        var usedRange = worksheet.RangeUsed();
        if (usedRange is not null)
        {
            usedRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        }

        workbook.SaveAs(filePath);
    }
}
