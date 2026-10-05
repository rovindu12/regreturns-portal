using RegReturns.Application.Returns;
using RegReturns.Domain.Templates;
using RegReturns.Infrastructure.Files;

using static RegReturns.UnitTests.Infrastructure.Files.ReturnFiles;

namespace RegReturns.UnitTests.Infrastructure.Files;

public sealed class ReturnFileRoundTripTests
{
    // Values a maker could have entered: numbers in several shapes, dates, booleans, blanks and text that looks like a
    // formula, starts with an apostrophe or needs CSV quoting.
    private static readonly (FieldDataType Type, string? Value)[] Values =
    [
        (FieldDataType.Amount, "1500.25"),
        (FieldDataType.Amount, "-12.50"),
        (FieldDataType.Amount, "1,234.5"),
        (FieldDataType.Percentage, "12.5%"),
        (FieldDataType.WholeNumber, "007"),
        (FieldDataType.Date, "2026-09-30"),
        (FieldDataType.Boolean, "Yes"),
        (FieldDataType.Amount, null),
        (FieldDataType.Text, "=1+1"),
        (FieldDataType.Text, "+44 20 7946 0000"),
        (FieldDataType.Text, "-"),
        (FieldDataType.Text, "@handle"),
        (FieldDataType.Text, "'quoted"),
        (FieldDataType.Text, "'=1+1"),
        (FieldDataType.Text, "Said \"no\", twice"),
        (FieldDataType.Text, "line one\nline two"),
    ];

    private static readonly ReturnFileSheet Sheet = new(
        "Monthly Liquidity Return (MLR), template version 1",
        "Example Bank (EXB), period 2026-09",
        [.. Values.Select((v, i) => new ReturnFileRow($"F{i + 1}", $"Field {i + 1}", "Section", v.Type, "VLD m", 2, v.Value))]);

    private static readonly KeyValuePair<string, string?>[] Expected =
        [.. Sheet.Rows.Select(r => new KeyValuePair<string, string?>(r.FieldCode, r.Value))];

    [Theory]
    [InlineData(ReturnFileFormat.Xlsx)]
    [InlineData(ReturnFileFormat.Csv)]
    public void A_downloaded_file_uploads_with_the_same_values(ReturnFileFormat format)
    {
        var file = new ReturnFileWriter().Write(format, Sheet);

        var read = Reader().Read("return" + ReturnFileFormats.ExtensionOf(format), file);

        read.Value.Format.ShouldBe(format);
        read.Value.Values.ShouldBe(Expected);
    }

    [Theory]
    [InlineData(ReturnFileFormat.Xlsx)]
    [InlineData(ReturnFileFormat.Csv)]
    public void A_downloaded_file_with_no_values_yet_uploads_as_all_blank(ReturnFileFormat format)
    {
        var blank = Sheet with { Rows = [.. Sheet.Rows.Select(r => r with { Value = null })] };
        var file = new ReturnFileWriter().Write(format, blank);

        var read = Reader().Read("return" + ReturnFileFormats.ExtensionOf(format), file);

        read.Value.Values.Select(v => v.Key).ShouldBe(Sheet.Rows.Select(r => r.FieldCode));
        read.Value.Values.ShouldAllBe(v => v.Value == null);
    }
}
