using RegReturns.Domain.Templates;

namespace RegReturns.UnitTests.Domain;

public sealed class FieldValueParserTests
{
    private static TemplateField Field(FieldDataType type, int precision = 2)
    {
        var template = TemplateVersion.CreateDraft(Guid.CreateVersion7(), 1, new DateOnly(2026, 1, 1));
        return template.AddField("F", "Field", "S", type, precision: precision).Value;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_values_are_blank_for_every_type(string? raw)
    {
        foreach (var type in Enum.GetValues<FieldDataType>())
        {
            FieldValueParser.Parse(Field(type), raw).State.ShouldBe(FieldValueState.Blank);
        }
    }

    [Theory]
    [InlineData("1234.5", 1234.5)]
    [InlineData(" 1234.50 ", 1234.5)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("1,234,567", 1234567)]
    [InlineData("-12", -12)]
    [InlineData("+12", 12)]
    [InlineData("0", 0)]
    [InlineData("12.500", 12.5)]
    public void Amounts_accept_invariant_numbers_with_optional_thousand_groups(string raw, double expected)
    {
        var parsed = FieldValueParser.Parse(Field(FieldDataType.Amount), raw);

        parsed.State.ShouldBe(FieldValueState.Valid);
        parsed.Number.ShouldBe((decimal)expected);
    }

    [Theory]
    [InlineData("1,2,3")]
    [InlineData("12,34")]
    [InlineData("1.234,56")]
    [InlineData("1 234")]
    [InlineData("12a")]
    [InlineData("1e5")]
    [InlineData("--1")]
    [InlineData(".5")]
    [InlineData("١٢")]
    [InlineData("(12)")]
    public void Amounts_reject_ambiguous_or_foreign_number_formats(string raw)
    {
        FieldValueParser.Parse(Field(FieldDataType.Amount), raw).ShouldBe(ParsedFieldValue.Invalid);
    }

    [Fact]
    public void Amounts_with_more_decimals_than_the_precision_are_invalid()
    {
        FieldValueParser.Parse(Field(FieldDataType.Amount, precision: 2), "1.234").State.ShouldBe(FieldValueState.Invalid);
    }

    [Fact]
    public void Amounts_larger_than_the_value_column_are_invalid()
    {
        FieldValueParser.Parse(Field(FieldDataType.Amount), "1,000,000,000,000,000").State.ShouldBe(FieldValueState.Invalid);
    }

    [Fact]
    public void The_largest_amount_the_column_holds_is_valid()
    {
        FieldValueParser.Parse(Field(FieldDataType.Amount, precision: 4), "999,999,999,999,999.9999").Number
            .ShouldBe(FieldValueParser.MaxMagnitude);
    }

    [Theory]
    [InlineData("12", true)]
    [InlineData("12.0", true)]
    [InlineData("12.5", false)]
    public void Whole_numbers_must_be_integral(string raw, bool valid)
    {
        FieldValueParser.Parse(Field(FieldDataType.WholeNumber), raw).State
            .ShouldBe(valid ? FieldValueState.Valid : FieldValueState.Invalid);
    }

    [Theory]
    [InlineData("12.5", 12.5)]
    [InlineData("12.5%", 12.5)]
    [InlineData("12.5 %", 12.5)]
    public void Percentages_may_end_in_a_percent_sign(string raw, double expected)
    {
        FieldValueParser.Parse(Field(FieldDataType.Percentage), raw).Number.ShouldBe((decimal)expected);
    }

    [Fact]
    public void Only_percentages_accept_a_percent_sign()
    {
        FieldValueParser.Parse(Field(FieldDataType.Amount), "12%").State.ShouldBe(FieldValueState.Invalid);
    }

    [Theory]
    [InlineData("2026-03-31", true)]
    [InlineData("2026-02-30", false)]
    [InlineData("31/03/2026", false)]
    [InlineData("2026-3-31", false)]
    public void Dates_must_be_iso_8601(string raw, bool valid)
    {
        var parsed = FieldValueParser.Parse(Field(FieldDataType.Date), raw);

        parsed.State.ShouldBe(valid ? FieldValueState.Valid : FieldValueState.Invalid);
        parsed.Number.ShouldBeNull();
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("YES", true)]
    [InlineData("False", true)]
    [InlineData("no", true)]
    [InlineData("1", false)]
    [InlineData("y", false)]
    public void Booleans_are_true_false_yes_or_no(string raw, bool valid)
    {
        FieldValueParser.Parse(Field(FieldDataType.Boolean), raw).State
            .ShouldBe(valid ? FieldValueState.Valid : FieldValueState.Invalid);
    }

    [Theory]
    [InlineData("yes", true)]
    [InlineData("TRUE", true)]
    [InlineData("no", false)]
    public void Boolean_words_map_to_their_value(string raw, bool expected)
    {
        FieldValueParser.TryParseBoolean(raw, out var value).ShouldBeTrue();
        value.ShouldBe(expected);
    }

    [Fact]
    public void Any_text_is_valid_for_text_fields()
    {
        var parsed = FieldValueParser.Parse(Field(FieldDataType.Text), "Anything at all, 1.2.3");

        parsed.State.ShouldBe(FieldValueState.Valid);
        parsed.Number.ShouldBeNull();
    }
}
