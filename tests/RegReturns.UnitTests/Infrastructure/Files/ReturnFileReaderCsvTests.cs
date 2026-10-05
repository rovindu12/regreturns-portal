using RegReturns.Application.Returns;

using static RegReturns.UnitTests.Infrastructure.Files.ReturnFiles;

namespace RegReturns.UnitTests.Infrastructure.Files;

public sealed class ReturnFileReaderCsvTests
{
    [Fact]
    public void Reads_values_in_file_order()
    {
        var content = ReadCsv("FieldCode,Value\r\nTOTAL_HQLA,1500.25\r\nNET_OUTFLOWS,1000\r\n").Value;

        content.Format.ShouldBe(ReturnFileFormat.Csv);
        content.ContentType.ShouldBe(ReturnFileFormats.CsvContentType);
        content.Values.ShouldBe([new("TOTAL_HQLA", "1500.25"), new("NET_OUTFLOWS", "1000")]);
    }

    [Fact]
    public void Columns_are_found_in_any_position_and_case_and_others_are_ignored()
    {
        ReadCsv("Notes, VALUE ,Label,fieldcode\r\nchecked,42,Total HQLA,TOTAL_HQLA\r\n").Value.Values
            .ShouldBe([new("TOTAL_HQLA", "42")]);
    }

    [Fact]
    public void A_header_below_title_lines_is_found()
    {
        ReadCsv("Monthly Liquidity Return\r\nExample Bank, 2026-09\r\n\r\nFieldCode,Value\r\nF1,1\r\n").Value.Values
            .ShouldBe([new("F1", "1")]);
    }

    [Fact]
    public void Quoted_fields_keep_commas_quotes_and_line_breaks()
    {
        var csv = "FieldCode,Value\r\nF1,\"1,234.50\"\r\nF2,\"Said \"\"no\"\"\"\r\nF3,\"line one\r\nline two\"\r\nF4,\"\"\r\n";

        ReadCsv(csv).Value.Values.ShouldBe(
            [new("F1", "1,234.50"), new("F2", "Said \"no\""), new("F3", "line one\r\nline two"), new("F4", null)]);
    }

    [Fact]
    public void A_byte_order_mark_is_ignored()
    {
        byte[] content = [0xEF, 0xBB, 0xBF, .. Utf8("FieldCode,Value\r\nF1,1\r\n")];

        Reader().Read("return.csv", content).Value.Values.ShouldBe([new("F1", "1")]);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    public void Lf_cr_and_crlf_line_endings_are_accepted(string lineBreak)
    {
        ReadCsv($"FieldCode,Value{lineBreak}F1,1{lineBreak}F2,2").Value.Values.ShouldBe([new("F1", "1"), new("F2", "2")]);
    }

    [Fact]
    public void Empty_and_missing_value_fields_read_as_null()
    {
        ReadCsv("FieldCode,Notes,Value\r\nF1,,\r\nF2\r\nF3,x,  \r\n").Value.Values
            .ShouldBe([new("F1", null), new("F2", null), new("F3", null)]);
    }

    [Fact]
    public void Codes_and_values_are_trimmed_and_codes_keep_their_case()
    {
        ReadCsv("FieldCode,Value\r\n total_hqla ,  12.50 \r\n").Value.Values.ShouldBe([new("total_hqla", "12.50")]);
    }

    [Fact]
    public void Rows_without_a_code_are_skipped()
    {
        ReadCsv("FieldCode,Value\r\nF1,1\r\n,2\r\n\r\nF2,3\r\n").Value.Values.ShouldBe([new("F1", "1"), new("F2", "3")]);
    }

    [Fact]
    public void A_quote_inside_an_unquoted_field_is_kept_as_text()
    {
        ReadCsv("FieldCode,Value\r\nF1,5\" pipe\r\n").Value.Values.ShouldBe([new("F1", "5\" pipe")]);
    }

    [Theory]
    [InlineData("'=1+1", "=1+1")]
    [InlineData("'@SUM(A1)", "@SUM(A1)")]
    [InlineData("'-", "-")]
    [InlineData("''=1+1", "'=1+1")]
    public void The_apostrophe_the_writer_adds_against_formula_injection_is_removed(string field, string expected)
    {
        ReadCsv($"FieldCode,Value\r\nF1,{field}\r\n").Value.Values.Single().Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("'quoted")]
    [InlineData("'-12.5")]
    [InlineData("'")]
    public void An_apostrophe_before_anything_else_is_kept(string field)
    {
        ReadCsv($"FieldCode,Value\r\nF1,{field}\r\n").Value.Values.Single().Value.ShouldBe(field);
    }

    [Fact]
    public void The_extension_is_matched_in_any_case()
    {
        Reader().Read("RETURN.CSV", Utf8("FieldCode,Value\r\nF1,1\r\n")).IsSuccess.ShouldBeTrue();
    }
}
