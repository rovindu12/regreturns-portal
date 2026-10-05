using ClosedXML.Excel;

using RegReturns.Application.Returns;

using static RegReturns.UnitTests.Infrastructure.Files.ReturnFiles;

namespace RegReturns.UnitTests.Infrastructure.Files;

public sealed class ReturnFileReaderXlsxTests
{
    private const string SheetEntry = "xl/worksheets/sheet1.xml";

    [Fact]
    public void Reads_values_in_file_order_under_a_header_below_title_lines()
    {
        var workbook = Workbook(sheet =>
        {
            sheet.Cell(1, 1).Value = "Monthly Liquidity Return (MLR), template version 1";
            sheet.Cell(2, 1).Value = "Example Bank (EXB), period 2026-09";
            sheet.Cell(4, 1).Value = "FieldCode";
            sheet.Cell(4, 2).Value = "Value";
            sheet.Cell(5, 1).Value = "TOTAL_HQLA";
            sheet.Cell(5, 2).Value = "1500.25";
            sheet.Cell(6, 1).Value = "NET_OUTFLOWS";
            sheet.Cell(6, 2).Value = "1000";
        });

        var content = ReadXlsx(workbook).Value;

        content.Format.ShouldBe(ReturnFileFormat.Xlsx);
        content.ContentType.ShouldBe(ReturnFileFormats.XlsxContentType);
        content.Values.ShouldBe([new("TOTAL_HQLA", "1500.25"), new("NET_OUTFLOWS", "1000")]);
    }

    [Fact]
    public void Columns_are_found_in_any_order_and_case_and_others_are_ignored()
    {
        var workbook = Workbook(sheet =>
        {
            sheet.Cell(1, 1).Value = " value ";
            sheet.Cell(1, 2).Value = "Label";
            sheet.Cell(1, 3).Value = "FIELDCODE";
            sheet.Cell(1, 4).Value = "Notes";
            sheet.Cell(2, 1).Value = "42";
            sheet.Cell(2, 2).Value = "Total HQLA";
            sheet.Cell(2, 3).Value = "TOTAL_HQLA";
            sheet.Cell(2, 4).Value = "checked";
        });

        ReadXlsx(workbook).Value.Values.ShouldBe([new("TOTAL_HQLA", "42")]);
    }

    [Theory]
    [InlineData(1234.5, "1234.5")]
    [InlineData(1_000_000d, "1000000")]
    [InlineData(-12.25, "-12.25")]
    [InlineData(0.1 + 0.2, "0.3")]
    [InlineData(1.5e20, "150000000000000000000")]
    [InlineData(0.000001, "0.000001")]
    public void Numbers_read_as_invariant_decimals_without_separators_or_exponents(double number, string expected)
    {
        ReadSingleXlsxValue(cell =>
        {
            cell.Value = number;
            cell.Style.NumberFormat.Format = "#,##0.00";
        }).ShouldBe(expected);
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void Booleans_read_as_true_or_false(bool value, string expected)
    {
        ReadSingleXlsxValue(cell => cell.Value = value).ShouldBe(expected);
    }

    [Fact]
    public void Dates_read_as_iso_dates()
    {
        ReadSingleXlsxValue(cell =>
        {
            cell.Value = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Unspecified);
            cell.Style.DateFormat.Format = "dd/mm/yyyy";
        }).ShouldBe("2026-03-31");
    }

    [Fact]
    public void Blank_cells_read_as_null()
    {
        ReadSingleXlsxValue(_ => { }).ShouldBeNull();
    }

    [Fact]
    public void Text_is_trimmed_and_whitespace_only_text_is_blank()
    {
        ReadXlsx(Table(("F1", "  12.50 "), ("F2", "   "))).Value.Values.ShouldBe([new("F1", "12.50"), new("F2", null)]);
    }

    [Theory]
    [InlineData(0.125, 10, "", "12.5%")]
    [InlineData(0.5, 9, "", "50%")]
    [InlineData(0.075, 0, "0.0%", "7.5%")]
    [InlineData(0.075, 0, "0.0\"%\"", "0.075")]
    public void Numbers_shown_as_percentages_read_as_percentages(double number, int builtInFormat, string customFormat, string expected)
    {
        ReadSingleXlsxValue(cell =>
        {
            cell.Value = number;
            if (customFormat.Length > 0)
            {
                cell.Style.NumberFormat.Format = customFormat;
            }
            else
            {
                cell.Style.NumberFormat.NumberFormatId = builtInFormat;
            }
        }).ShouldBe(expected);
    }

    [Fact]
    public void Formulas_are_not_evaluated()
    {
        ReadSingleXlsxValue(cell => cell.FormulaA1 = "1+2").ShouldBeNull();
    }

    [Fact]
    public void A_formula_reads_as_the_value_saved_with_it_even_when_that_value_is_stale()
    {
        var workbook = Workbook(sheet =>
        {
            sheet.Cell(1, 1).Value = "FieldCode";
            sheet.Cell(1, 2).Value = "Value";
            sheet.Cell(2, 1).Value = "F1";
            sheet.Cell(2, 2).FormulaA1 = "2*3";
        });

        // ClosedXML saves formulas without a cached value; Excel saves the last result next to the formula.
        var sheetXml = EntryText(workbook, SheetEntry).Replace("<x:f>2*3</x:f>", "<x:f>2*3</x:f><x:v>5</x:v>", StringComparison.Ordinal);

        ReadXlsx(WithEntries(workbook, (SheetEntry, Utf8(sheetXml)))).Value.Values.ShouldBe([new("F1", "5")]);
    }

    [Fact]
    public void Error_values_are_kept_as_text_for_validation_to_report()
    {
        ReadSingleXlsxValue(cell => cell.Value = XLError.DivisionByZero).ShouldBe("#DIV/0!");
    }

    [Fact]
    public void Rows_without_a_code_are_skipped()
    {
        ReadXlsx(Table(("F1", 1), (null, 2), ("  ", 3), ("F2", 4))).Value.Values.ShouldBe([new("F1", "1"), new("F2", "4")]);
    }

    [Fact]
    public void Codes_are_kept_as_trimmed_without_changing_case()
    {
        ReadXlsx(Table((" total_hqla ", 1))).Value.Values.Single().Key.ShouldBe("total_hqla");
    }

    [Fact]
    public void A_number_in_the_code_column_reads_as_text()
    {
        var workbook = Workbook(sheet =>
        {
            sheet.Cell(1, 1).Value = "FieldCode";
            sheet.Cell(1, 2).Value = "Value";
            sheet.Cell(2, 1).Value = 100;
            sheet.Cell(2, 2).Value = 7;
        });

        ReadXlsx(workbook).Value.Values.ShouldBe([new("100", "7")]);
    }

    [Fact]
    public void Only_the_first_worksheet_is_read()
    {
        var workbook = Workbook(
            sheet =>
            {
                sheet.Cell(1, 1).Value = "FieldCode";
                sheet.Cell(1, 2).Value = "Value";
                sheet.Cell(2, 1).Value = "F1";
                sheet.Cell(2, 2).Value = "first";
            },
            beforeSave: wb =>
            {
                var second = wb.AddWorksheet("Notes");
                second.Cell(1, 1).Value = "FieldCode";
                second.Cell(1, 2).Value = "Value";
                second.Cell(2, 1).Value = "F2";
                second.Cell(2, 2).Value = "second";
            });

        ReadXlsx(workbook).Value.Values.ShouldBe([new("F1", "first")]);
    }

    [Fact]
    public void The_extension_is_matched_in_any_case()
    {
        Reader().Read("RETURN.XLSX", Table(("F1", 1))).IsSuccess.ShouldBeTrue();
    }
}
