using System.Text;

using RegReturns.Infrastructure.Files;

namespace RegReturns.UnitTests.Infrastructure.Files;

public sealed class CsvTextTests
{
    [Fact]
    public void Plain_cells_are_joined_with_commas_and_end_with_crlf()
    {
        Line("MLR", "HLB", "2024-01").ShouldBe("MLR,HLB,2024-01\r\n");
    }

    [Fact]
    public void A_null_cell_is_empty()
    {
        Line("a", null, "c").ShouldBe("a,,c\r\n");
    }

    [Theory]
    [InlineData("Late, see letter", "\"Late, see letter\"")]
    [InlineData("12\" pipe", "\"12\"\" pipe\"")]
    [InlineData("line one\nline two", "\"line one\nline two\"")]
    [InlineData("line one\r\nline two", "\"line one\r\nline two\"")]
    public void Cells_with_commas_quotes_or_line_breaks_are_quoted(string cell, string written)
    {
        Line(cell).ShouldBe(written + "\r\n");
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+cmd", "'+cmd")]
    [InlineData("-cmd", "'-cmd")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tTAB", "'\tTAB")]
    [InlineData("'=1+1", "''=1+1")]
    public void Cells_a_spreadsheet_could_run_as_formulas_get_an_apostrophe(string cell, string written)
    {
        Line(cell).ShouldBe(written + "\r\n");
    }

    [Theory]
    [InlineData("-12.5")]
    [InlineData("+3")]
    [InlineData("-0")]
    [InlineData("plain")]
    public void Signed_numbers_and_plain_text_are_written_as_they_are(string cell)
    {
        Line(cell).ShouldBe(cell + "\r\n");
    }

    [Fact]
    public void A_formula_with_a_comma_is_guarded_then_quoted()
    {
        Line("=SUM(A1,A2)").ShouldBe("\"'=SUM(A1,A2)\"\r\n");
    }

    [Fact]
    public void Lines_are_appended_to_the_text_so_far()
    {
        var text = new StringBuilder("Header\r\n");

        CsvText.AppendLine(text, ["a", "b"]);
        CsvText.AppendLine(text, ["c"]);

        text.ToString().ShouldBe("Header\r\na,b\r\nc\r\n");
    }

    [Fact]
    public void Written_lines_read_back_to_the_same_cells()
    {
        string?[] cells = ["HLB", "Late, see \"letter\"", "=SUM(A1)", "-12.5", "two\r\nlines", null];
        var text = new StringBuilder();
        CsvText.AppendLine(text, cells);

        var read = new CsvParser(text.ToString(), ReturnFileLimits.Default).ReadRecord().Value;

        read.Select(CsvFormulaGuard.Unprotect).ShouldBe(cells.Select(c => c ?? string.Empty));
    }

    [Fact]
    public void Text_is_encoded_as_utf8_after_a_byte_order_mark()
    {
        var bytes = CsvText.ToUtf8WithBom("Bänk,1\r\n");

        bytes.ShouldBe([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("Bänk,1\r\n")]);
    }

    private static string Line(params string?[] cells)
    {
        var text = new StringBuilder();
        CsvText.AppendLine(text, cells);
        return text.ToString();
    }
}
