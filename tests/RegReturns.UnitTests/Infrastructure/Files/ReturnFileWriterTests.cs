using System.Text;

using ClosedXML.Excel;

using RegReturns.Application.Returns;
using RegReturns.Domain.Templates;
using RegReturns.Infrastructure.Files;

namespace RegReturns.UnitTests.Infrastructure.Files;

public sealed class ReturnFileWriterTests
{
    private const string Title = "Monthly Liquidity Return (MLR), template version 1";
    private const string Subtitle = "Example Bank (EXB), period 2026-09, due 2026-10-15";

    private static readonly ReturnFileSheet Sheet = new(Title, Subtitle,
    [
        new ReturnFileRow("TOTAL_HQLA", "Total high-quality liquid assets", "Stock of HQLA", FieldDataType.Amount, "VLD m", 2, "1500.25"),
        new ReturnFileRow("LCR", "Liquidity coverage ratio", "Ratios", FieldDataType.Percentage, "%", 2, null),
        new ReturnFileRow("REPORT_DATE", "Reporting date", "General", FieldDataType.Date, "", 0, "2026-09-30"),
    ]);

    private readonly ReturnFileWriter _writer = new();

    [Fact]
    public void Workbook_has_the_title_block_above_the_header_on_row_4()
    {
        using var workbook = OpenWorkbook(Sheet);
        var sheet = workbook.Worksheet(1);

        sheet.Name.ShouldBe(ReturnFileWriter.SheetName);
        sheet.Cell(1, 1).GetText().ShouldBe(Title);
        sheet.Cell(1, 1).Style.Font.Bold.ShouldBeTrue();
        sheet.Cell(2, 1).GetText().ShouldBe(Subtitle);
        sheet.Row(3).IsEmpty().ShouldBeTrue();
        Enumerable.Range(1, 6).Select(c => sheet.Cell(4, c).GetText())
            .ShouldBe(["FieldCode", "Section", "Label", "Unit", "Type", "Value"]);
    }

    [Fact]
    public void Workbook_has_one_row_per_field_in_order()
    {
        using var workbook = OpenWorkbook(Sheet);
        var sheet = workbook.Worksheet(1);

        Enumerable.Range(1, 6).Select(c => sheet.Cell(5, c).GetText())
            .ShouldBe(["TOTAL_HQLA", "Stock of HQLA", "Total high-quality liquid assets", "VLD m", "Amount (2 dp)", "1500.25"]);
        sheet.Cell(6, 1).GetText().ShouldBe("LCR");
        sheet.Cell(6, 6).IsEmpty().ShouldBeTrue();
        sheet.Cell(7, 1).GetText().ShouldBe("REPORT_DATE");
        sheet.LastRowUsed()!.RowNumber().ShouldBe(7);
    }

    [Fact]
    public void Workbook_value_column_is_formatted_as_text_including_cells_still_to_fill()
    {
        using var workbook = OpenWorkbook(Sheet);
        var sheet = workbook.Worksheet(1);

        sheet.Column(6).Style.NumberFormat.Format.ShouldBe("@");
        sheet.Cell(5, 6).Style.NumberFormat.Format.ShouldBe("@");
        sheet.Cell(6, 6).Style.NumberFormat.Format.ShouldBe("@");
        sheet.Cell(7, 6).CachedValue.Type.ShouldBe(XLDataType.Text);
    }

    [Fact]
    public void Workbook_freezes_the_rows_down_to_the_header()
    {
        using var workbook = OpenWorkbook(Sheet);

        workbook.Worksheet(1).SheetView.SplitRow.ShouldBe(ReturnFileWriter.HeaderRow);
    }

    [Fact]
    public void Workbook_is_not_protected_and_has_readable_column_widths()
    {
        using var workbook = OpenWorkbook(Sheet);
        var sheet = workbook.Worksheet(1);

        sheet.IsProtected.ShouldBeFalse();
        Enumerable.Range(1, 6).ShouldAllBe(c => sheet.Column(c).Width >= 10);
    }

    [Theory]
    [InlineData("=1+1")]
    [InlineData("=HYPERLINK(\"http://example.invalid\",\"x\")")]
    [InlineData("+SUM(A1:A2)")]
    public void Workbook_stores_formula_like_values_as_text(string value)
    {
        using var workbook = OpenWorkbook(SheetWithValue(value));
        var cell = workbook.Worksheet(1).Cell(5, 6);

        cell.HasFormula.ShouldBeFalse();
        cell.GetText().ShouldBe(value);
    }

    [Theory]
    [InlineData(FieldDataType.Amount, 2, "Amount (2 dp)")]
    [InlineData(FieldDataType.Percentage, 1, "Percentage (1 dp)")]
    [InlineData(FieldDataType.WholeNumber, 0, "Whole number")]
    [InlineData(FieldDataType.Date, 0, "Date (yyyy-MM-dd)")]
    [InlineData(FieldDataType.Boolean, 0, "Yes/No")]
    [InlineData(FieldDataType.Text, 0, "Text")]
    public void Type_column_describes_the_data_type(FieldDataType dataType, int precision, string expected)
    {
        ReturnFileWriter.DescribeType(dataType, precision).ShouldBe(expected);
    }

    [Fact]
    public void Every_data_type_has_a_description()
    {
        Enum.GetValues<FieldDataType>().ShouldAllBe(t => ReturnFileWriter.DescribeType(t, 2).Length > 0);
    }

    [Fact]
    public void Csv_starts_with_a_byte_order_mark()
    {
        _writer.Write(ReturnFileFormat.Csv, Sheet).Take(3).ShouldBe(new byte[] { 0xEF, 0xBB, 0xBF });
    }

    [Fact]
    public void Csv_has_the_header_then_one_crlf_line_per_field_and_no_title_lines()
    {
        CsvText(Sheet).ShouldBe(
            "FieldCode,Section,Label,Unit,Type,Value\r\n"
            + "TOTAL_HQLA,Stock of HQLA,Total high-quality liquid assets,VLD m,Amount (2 dp),1500.25\r\n"
            + "LCR,Ratios,Liquidity coverage ratio,%,Percentage (2 dp),\r\n"
            + "REPORT_DATE,General,Reporting date,,Date (yyyy-MM-dd),2026-09-30\r\n");
    }

    [Theory]
    [InlineData("1,234.50", "\"1,234.50\"")]
    [InlineData("Said \"no\"", "\"Said \"\"no\"\"\"")]
    [InlineData("line one\nline two", "\"line one\nline two\"")]
    [InlineData("plain text", "plain text")]
    public void Csv_quotes_fields_with_commas_quotes_and_line_breaks(string value, string expected)
    {
        ValueField(value).ShouldBe(expected);
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+SUM(A1:A2)", "'+SUM(A1:A2)")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("\tx", "'\tx")]
    [InlineData("\rx", "\"'\rx\"")]
    [InlineData("'=1+1", "''=1+1")]
    public void Csv_prefixes_cells_a_spreadsheet_could_run_as_formulas(string value, string expected)
    {
        ValueField(value).ShouldBe(expected);
    }

    [Theory]
    [InlineData("-12.5")]
    [InlineData("+3")]
    [InlineData("1234.5")]
    [InlineData("'quoted")]
    public void Csv_leaves_plain_numbers_and_other_text_alone(string value)
    {
        ValueField(value).ShouldBe(value);
    }

    [Fact]
    public void Csv_neutralises_formulas_in_every_column()
    {
        var sheet = new ReturnFileSheet(Title, Subtitle,
            [new ReturnFileRow("F1", "=Label", "@Section", FieldDataType.Text, "+Unit", 0, null)]);

        CsvText(sheet).Split("\r\n")[1].ShouldBe("F1,'@Section,'=Label,'+Unit,Text,");
    }

    [Fact]
    public void An_unknown_format_is_a_programming_error()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => _writer.Write((ReturnFileFormat)99, Sheet));
    }

    private static ReturnFileSheet SheetWithValue(string value) =>
        new(Title, Subtitle, [new ReturnFileRow("F1", "Field", "Section", FieldDataType.Text, "", 0, value)]);

    private XLWorkbook OpenWorkbook(ReturnFileSheet sheet) =>
        new(new MemoryStream(_writer.Write(ReturnFileFormat.Xlsx, sheet)));

    private string CsvText(ReturnFileSheet sheet) => Encoding.UTF8.GetString(_writer.Write(ReturnFileFormat.Csv, sheet).AsSpan(3));

    private string ValueField(string value)
    {
        var line = CsvText(SheetWithValue(value))["FieldCode,Section,Label,Unit,Type,Value\r\n".Length..];
        return line[..^2]["F1,Section,Field,,Text,".Length..];
    }
}
