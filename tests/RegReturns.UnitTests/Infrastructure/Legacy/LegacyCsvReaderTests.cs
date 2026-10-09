using System.Security.Cryptography;
using System.Text;

using RegReturns.Application.Migration;
using RegReturns.Infrastructure.Legacy;

namespace RegReturns.UnitTests.Infrastructure.Legacy;

public sealed class LegacyCsvReaderTests
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    [Fact]
    public void The_header_is_trimmed_and_data_rows_are_numbered_from_line_2()
    {
        var table = Read("Bank , Period End,Assets\nHLB,31/01/2024,100\nCCB,31/01/2024,200\n");

        table.FileName.ShouldBe("returns.csv");
        table.Header.ShouldBe(["Bank", "Period End", "Assets"]);
        table.Rows.Select(r => r.LineNumber).ShouldBe([2, 3]);
        table.Rows[1].Cells.ShouldBe(["CCB", "31/01/2024", "200"]);
    }

    [Fact]
    public void Data_cells_are_kept_as_found()
    {
        var table = Read("Bank,Assets\n HLB , VLD 1 \n");

        table.Rows[0].Cells.ShouldBe([" HLB ", " VLD 1 "]);
    }

    [Fact]
    public void A_byte_order_mark_is_not_part_of_the_first_header()
    {
        var table = LegacyCsvReader.Read("returns.csv", [.. Bom, .. Encoding.UTF8.GetBytes("Bank,Assets\r\nHLB,100\r\n")]).Value;

        table.Header[0].ShouldBe("Bank");
    }

    [Fact]
    public void Crlf_and_lf_line_ends_read_the_same()
    {
        var crlf = Read("Bank,Assets\r\nHLB,100\r\nCCB,200\r\n");
        var lf = Read("Bank,Assets\nHLB,100\nCCB,200\n");

        crlf.Header.ShouldBe(lf.Header);
        Flatten(crlf).ShouldBe(Flatten(lf));
    }

    [Fact]
    public void A_missing_final_line_break_loses_no_row()
    {
        Read("Bank,Assets\nHLB,100").Rows.ShouldHaveSingleItem().Cells.ShouldBe(["HLB", "100"]);
    }

    [Fact]
    public void A_trailing_empty_line_is_a_blank_row()
    {
        var table = Read("Bank,Assets\r\nHLB,100\r\n\r\n");

        table.Rows.Count.ShouldBe(2);
        table.Rows[1].LineNumber.ShouldBe(3);
        table.Rows[1].IsBlank.ShouldBeTrue();
    }

    [Fact]
    public void A_row_of_empty_cells_is_blank()
    {
        Read("Bank,Assets\n , \n").Rows[0].IsBlank.ShouldBeTrue();
    }

    [Fact]
    public void A_row_with_any_value_is_not_blank()
    {
        Read("Bank,Assets\n,0\n").Rows[0].IsBlank.ShouldBeFalse();
    }

    [Fact]
    public void Quoted_cells_keep_commas_quotes_and_line_breaks_and_the_line_count_follows()
    {
        var table = Read("Bank,Remarks\nHLB,\"Late, see \"\"letter\"\"\nline two\"\nCCB,none\n");

        table.Rows[0].Cells.ShouldBe(["HLB", "Late, see \"letter\"\nline two"]);
        table.Rows[1].LineNumber.ShouldBe(4);
    }

    [Fact]
    public void The_digest_covers_the_file_bytes_with_the_byte_order_mark()
    {
        byte[] content = [.. Bom, .. Encoding.UTF8.GetBytes("Bank,Assets\r\nHLB,100\r\n")];

        var table = LegacyCsvReader.Read("returns.csv", content).Value;

        table.Sha256.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(content)));
    }

    [Theory]
    [InlineData(new byte[] { 0x42, 0x61, 0x6E, 0x6B, 0x00, 0x0A })]
    [InlineData(new byte[] { 0x42, 0x61, 0xC3, 0x28, 0x0A })]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37 })]
    [InlineData(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00 })]
    public void Content_that_is_not_utf8_text_is_refused(byte[] content)
    {
        Refusal(content).ShouldBe("returns.csv cannot be read: it is not UTF-8 text.");
    }

    [Fact]
    public void A_latin1_export_is_refused_rather_than_misread()
    {
        var latin1 = Encoding.Latin1.GetBytes("Bank,Assets\nBänk,100\n");

        Refusal(latin1).ShouldBe("returns.csv cannot be read: it is not UTF-8 text.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_empty_file_is_refused(bool withByteOrderMark)
    {
        Refusal(withByteOrderMark ? Bom : []).ShouldBe("returns.csv cannot be read: it is empty.");
    }

    [Fact]
    public void An_unclosed_quote_is_refused_naming_its_line()
    {
        Refusal(Encoding.UTF8.GetBytes("Bank,Remarks\nHLB,ok\nCCB,\"never closed\n")).ShouldContain("starting on line 3");
    }

    [Fact]
    public void A_header_that_is_not_csv_is_refused()
    {
        Refusal(Encoding.UTF8.GetBytes("Bank,\"Assets\"x\nHLB,100\n")).ShouldContain("Line 1 of the file is not valid CSV");
    }

    [Fact]
    public void A_cell_longer_than_the_csv_limit_is_refused()
    {
        Refusal(Encoding.UTF8.GetBytes("Bank,Remarks\nHLB," + new string('r', 4_001) + "\n")).ShouldContain("longer than 4,000 characters");
    }

    [Fact]
    public void A_file_larger_than_the_limit_is_refused_before_it_is_read()
    {
        var content = new byte[LegacyCsvReader.MaxFileBytes + 1];

        Refusal(content).ShouldBe("returns.csv cannot be read: it is larger than 64 MB.");
    }

    private static LegacyTable Read(string text) => LegacyCsvReader.Read("returns.csv", Encoding.UTF8.GetBytes(text)).Value;

    private static string Refusal(byte[] content)
    {
        var error = LegacyCsvReader.Read("returns.csv", content).Error.ShouldNotBeNull();
        error.Code.ShouldBe(MigrationErrors.FileUnreadable.Code);
        return error.Message;
    }

    private static IEnumerable<(int Line, string Cells)> Flatten(LegacyTable table) =>
        table.Rows.Select(r => (r.LineNumber, string.Join('|', r.Cells)));
}
