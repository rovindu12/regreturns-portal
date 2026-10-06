using System.Buffers;
using System.Globalization;
using System.Text;

using ClosedXML.Excel;

using RegReturns.Application.Returns;
using RegReturns.Domain.Templates;

namespace RegReturns.Infrastructure.Files;

/// <summary>
/// Writes a return's template for download. The workbook has a title block, a header row and one row per field, with a
/// text-formatted Value column; the CSV is UTF-8 with a byte order mark, CRLF line endings, RFC 4180 quoting and
/// formula injection neutralised. Both read back through <see cref="ReturnFileReader"/> with the same values.
/// </summary>
public sealed class ReturnFileWriter : IReturnFileWriter
{
    /// <summary>The name of the workbook's only worksheet.</summary>
    public const string SheetName = "Return";

    /// <summary>The workbook row holding the column headers (below the title, subtitle and a blank row).</summary>
    public const int HeaderRow = 4;

    /// <summary>The Section column header.</summary>
    internal const string SectionColumn = "Section";

    /// <summary>The Label column header.</summary>
    internal const string LabelColumn = "Label";

    /// <summary>The Unit column header.</summary>
    internal const string UnitColumn = "Unit";

    /// <summary>The Type column header.</summary>
    internal const string TypeColumn = "Type";

    /// <summary>Excel's number format for text.</summary>
    internal const string TextNumberFormat = "@";

    private const char ExcelQuotePrefix = '\'';
    private const string CsvLineBreak = "\r\n";
    private const char CsvSeparator = ',';
    private const string CsvQuote = "\"";
    private const string CsvEscapedQuote = "\"\"";

    private static readonly (string Name, double Width)[] Columns =
    [
        (ReturnFileFormats.FieldCodeColumn, 22),
        (SectionColumn, 28),
        (LabelColumn, 60),
        (UnitColumn, 14),
        (TypeColumn, 20),
        (ReturnFileFormats.ValueColumn, 22),
    ];

    private static readonly SearchValues<char> CsvSpecials = SearchValues.Create(",\"\r\n");
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <inheritdoc />
    public byte[] Write(ReturnFileFormat format, ReturnFileSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return format switch
        {
            ReturnFileFormat.Xlsx => WriteXlsx(sheet),
            ReturnFileFormat.Csv => WriteCsv(sheet),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown return file format."),
        };
    }

    /// <summary>Describes a field's data type for the Type column.</summary>
    /// <param name="dataType">The data type.</param>
    /// <param name="precision">Maximum decimal places for numbers.</param>
    /// <returns>For example <c>Amount (2 dp)</c> or <c>Yes/No</c>.</returns>
    internal static string DescribeType(FieldDataType dataType, int precision) => dataType switch
    {
        FieldDataType.Amount => string.Create(CultureInfo.InvariantCulture, $"Amount ({precision} dp)"),
        FieldDataType.Percentage => string.Create(CultureInfo.InvariantCulture, $"Percentage ({precision} dp)"),
        FieldDataType.WholeNumber => "Whole number",
        FieldDataType.Date => $"Date ({FieldValueParser.DateFormat})",
        FieldDataType.Boolean => "Yes/No",
        FieldDataType.Text => "Text",
        _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, "Unknown field data type."),
    };

    private static string?[] CellsOf(ReturnFileRow row) =>
        [row.FieldCode, row.Section, row.Label, row.Unit, DescribeType(row.DataType, row.Precision), row.Value];

    private static byte[] WriteXlsx(ReturnFileSheet sheet)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet(SheetName);

        // Set before any value so every cell in the column, including ones the maker fills in later, is text:
        // Excel then keeps 12.50 or 2026-03-31 as typed instead of turning them into numbers or date serials.
        worksheet.Column(Columns.Length).Style.NumberFormat.Format = TextNumberFormat;

        SetText(worksheet.Cell(1, 1), sheet.Title);
        worksheet.Cell(1, 1).Style.Font.Bold = true;
        SetText(worksheet.Cell(2, 1), sheet.Subtitle);

        for (var column = 1; column <= Columns.Length; column++)
        {
            SetText(worksheet.Cell(HeaderRow, column), Columns[column - 1].Name);
            worksheet.Column(column).Width = Columns[column - 1].Width;
        }

        worksheet.Range(HeaderRow, 1, HeaderRow, Columns.Length).Style.Font.Bold = true;

        var row = HeaderRow;
        foreach (var field in sheet.Rows)
        {
            row++;
            var cells = CellsOf(field);
            for (var column = 1; column <= cells.Length; column++)
            {
                SetText(worksheet.Cell(row, column), cells[column - 1]);
            }
        }

        worksheet.SheetView.FreezeRows(HeaderRow);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Stores text in a cell as text, never as a formula, keeping a leading apostrophe. Every workbook RegReturns writes
    /// puts strings in cells through this.
    /// </summary>
    /// <param name="cell">The cell.</param>
    /// <param name="text">The text; <see langword="null"/> leaves the cell empty.</param>
    internal static void SetText(IXLCell cell, string? text)
    {
        if (text is null)
        {
            return;
        }

        // A string value is always stored as text, never as a formula. ClosedXML turns one leading apostrophe into
        // Excel's quote prefix and drops it, so a value that starts with one gets a second to keep it.
        cell.Value = text.StartsWith(ExcelQuotePrefix) ? ExcelQuotePrefix + text : text;
    }

    private static byte[] WriteCsv(ReturnFileSheet sheet)
    {
        var builder = new StringBuilder();
        AppendCsvLine(builder, [.. Columns.Select(c => c.Name)]);
        foreach (var row in sheet.Rows)
        {
            AppendCsvLine(builder, CellsOf(row));
        }

        return [.. Utf8Bom, .. Encoding.UTF8.GetBytes(builder.ToString())];
    }

    private static void AppendCsvLine(StringBuilder builder, string?[] cells)
    {
        for (var i = 0; i < cells.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(CsvSeparator);
            }

            var text = CsvFormulaGuard.Protect(cells[i] ?? string.Empty);
            builder.Append(text.AsSpan().ContainsAny(CsvSpecials)
                ? CsvQuote + text.Replace(CsvQuote, CsvEscapedQuote, StringComparison.Ordinal) + CsvQuote
                : text);
        }

        builder.Append(CsvLineBreak);
    }
}
