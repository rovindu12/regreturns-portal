using System.Globalization;

using ClosedXML.Excel;

using RegReturns.Application.Returns;
using RegReturns.Domain.Common;
using RegReturns.Domain.Templates;

namespace RegReturns.Infrastructure.Files;

/// <summary>
/// Reads the <c>FieldCode</c>/<c>Value</c> table from the first worksheet of a workbook that passed
/// <see cref="XlsxPackageInspector"/>. Cells are read as text from their cached values: formulas are never evaluated.
/// </summary>
internal static class XlsxTableReader
{
    private const string TrueText = "true";
    private const string FalseText = "false";
    private const string PercentSuffix = "%";
    private const string TimeFormat = "c";

    // Fixed-point with up to 28 decimals: no exponent, no group separators, no trailing zeros.
    private const string NumberFormat = "0.############################";

    // Excel's built-in "0%" and "0.00%" number formats.
    private const int BuiltInPercent = 9;
    private const int BuiltInPercentTwoDecimals = 10;

    // No return figure comes near this; it also keeps a percentage times 100 inside decimal's range.
    private const double MaxMagnitude = 1e27;

    /// <summary>Reads the workbook.</summary>
    /// <param name="workbookStream">The package, positioned at the start.</param>
    /// <param name="limits">The limits.</param>
    /// <returns>The values, or why the file is refused.</returns>
    public static Result<ReturnFileContent> Read(Stream workbookStream, ReturnFileLimits limits)
    {
        using var workbook = new XLWorkbook(workbookStream);
        if (workbook.Worksheets.Count == 0)
        {
            return UploadErrors.NoHeader;
        }

        var sheet = workbook.Worksheet(1);
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var header = FindHeader(sheet, Math.Min(lastRow, limits.HeaderSearchRows));
        if (header is not { } found)
        {
            return UploadErrors.NoHeader;
        }

        var table = new FieldTable(limits);
        for (var row = found.Row + 1; row <= lastRow; row++)
        {
            if (!TryReadText(sheet.Cell(row, found.Columns.Code), out var code)
                || !TryReadText(sheet.Cell(row, found.Columns.Value), out var value))
            {
                return ReadErrors.NumberOutOfRange;
            }

            if (table.Add(code, value) is { } refused)
            {
                return refused;
            }
        }

        return table.ToContent(ReturnFileFormat.Xlsx);
    }

    /// <summary>Converts a cell's cached value to text.</summary>
    /// <param name="cell">The cell.</param>
    /// <param name="text">The trimmed text, or <see langword="null"/> for a blank cell.</param>
    /// <returns><see langword="false"/> for a number too large to be a return figure.</returns>
    internal static bool TryReadText(IXLCell cell, out string? text)
    {
        var value = cell.CachedValue;
        switch (value.Type)
        {
            case XLDataType.Blank:
                text = null;
                return true;
            case XLDataType.Boolean:
                text = value.GetBoolean() ? TrueText : FalseText;
                return true;
            case XLDataType.Number:
                return TryFormatNumber(value.GetNumber(), IsPercentFormat(cell.Style.NumberFormat), out text);
            case XLDataType.DateTime:
                text = value.GetDateTime().ToString(FieldValueParser.DateFormat, CultureInfo.InvariantCulture);
                return true;
            case XLDataType.TimeSpan:
                text = value.GetTimeSpan().ToString(TimeFormat, CultureInfo.InvariantCulture);
                return true;
            case XLDataType.Text:
                text = FieldTable.Normalize(value.GetText());
                return true;
            default:
                // An error value such as #DIV/0!: kept as text, so validation reports the field as invalid.
                text = value.ToString(CultureInfo.InvariantCulture);
                return true;
        }
    }

    private static (int Row, HeaderColumns Columns)? FindHeader(IXLWorksheet sheet, int lastSearchRow)
    {
        for (var row = 1; row <= lastSearchRow; row++)
        {
            var cells = sheet.Row(row).CellsUsed()
                .Select(c => (c.Address.ColumnNumber, c.CachedValue.IsText ? c.CachedValue.GetText() : null));
            if (FieldTable.MatchHeader(cells) is { } columns)
            {
                return (row, columns);
            }
        }

        return null;
    }

    private static bool TryFormatNumber(double number, bool percent, out string? text)
    {
        if (!double.IsFinite(number) || Math.Abs(number) >= MaxMagnitude)
        {
            text = null;
            return false;
        }

        // decimal keeps 15 significant digits, so binary noise such as 0.30000000000000004 reads as 0.3.
        var value = (decimal)number;
        text = percent
            ? (value * 100).ToString(NumberFormat, CultureInfo.InvariantCulture) + PercentSuffix
            : value.ToString(NumberFormat, CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>
    /// Returns whether a number format shows the value as a percentage, so 0.125 shown as 12.5% reads as
    /// <c>12.5%</c> rather than <c>0.125</c>.
    /// </summary>
    /// <param name="format">The cell's number format.</param>
    /// <returns><see langword="true"/> for a percentage format.</returns>
    internal static bool IsPercentFormat(IXLNumberFormat format)
    {
        if (format.NumberFormatId is BuiltInPercent or BuiltInPercentTwoDecimals)
        {
            return true;
        }

        // A % outside quotes and not escaped with a backslash multiplies by 100.
        var code = format.Format ?? string.Empty;
        var quoted = false;
        var escaped = false;
        foreach (var c in code)
        {
            if (escaped)
            {
                escaped = false;
            }
            else if (c == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && c == '\\')
            {
                escaped = true;
            }
            else if (!quoted && c == '%')
            {
                return true;
            }
        }

        return false;
    }
}
