using System.Globalization;

using RegReturns.Infrastructure.Legacy;

namespace RegReturns.UnitTests.Infrastructure.Legacy;

public sealed class LegacyCleansingTests
{
    private static readonly CleansingRules Rules = LegacyTestData.Rules;

    private static readonly CleansingRules NoExcelDates = Rules with { ExcelSerialDates = false };

    [Theory]
    [InlineData("2024-11-30", "2024-11-30T00:00:00")]
    [InlineData("2024-12-09 14:05:30", "2024-12-09T14:05:30")]
    [InlineData("30/11/2024", "2024-11-30T00:00:00")]
    [InlineData("09/12/2024 14:05", "2024-12-09T14:05:00")]
    [InlineData("9/1/2025", "2025-01-09T00:00:00")]
    [InlineData("30-Nov-2024", "2024-11-30T00:00:00")]
    [InlineData("30.11.2024", "2024-11-30T00:00:00")]
    [InlineData("20241130", "2024-11-30T00:00:00")]
    [InlineData("Nov 30, 2024", "2024-11-30T00:00:00")]
    public void Dates_are_read_in_every_mapping_format(string raw, string expected)
    {
        ReadsDateAs(raw, expected);
    }

    [Theory]
    [InlineData("30-NOV-2024")]
    [InlineData("30-nov-2024")]
    [InlineData("NOV 30, 2024")]
    public void Month_names_are_read_in_any_case(string raw)
    {
        ReadsDateAs(raw, "2024-11-30T00:00:00");
    }

    [Theory]
    [InlineData("  31/12/2024  ")]
    [InlineData("Dec\u00A031, 2024")]
    [InlineData("Dec  31,   2024")]
    public void Dates_are_read_through_padding_and_odd_spaces(string raw)
    {
        ReadsDateAs(raw, "2024-12-31T00:00:00");
    }

    [Theory]
    [InlineData("45292", "2024-01-01T00:00:00")]
    [InlineData("45626", "2024-11-30T00:00:00")]
    [InlineData("45626.5", "2024-11-30T12:00:00")]
    public void Five_digit_numbers_are_excel_serial_dates_when_the_mapping_allows_them(string raw, string expected)
    {
        LegacyCleansing.TryParseDate(raw, Rules, out var date).ShouldBeTrue();

        date.ShouldBe(DateTime.FromOADate(double.Parse(raw, CultureInfo.InvariantCulture)));
        date!.Value.ToString("s", CultureInfo.InvariantCulture).ShouldBe(expected);
    }

    [Fact]
    public void Excel_serial_dates_are_refused_when_the_mapping_does_not_allow_them()
    {
        LegacyCleansing.TryParseDate("45626", NoExcelDates, out var date).ShouldBeFalse();

        date.ShouldBeNull();
    }

    [Theory]
    [InlineData("4562")]
    [InlineData("456260")]
    [InlineData("45626.")]
    [InlineData("-45626")]
    public void Only_five_digit_numbers_count_as_excel_serial_dates(string raw)
    {
        LegacyCleansing.TryParseDate(raw, Rules, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("31/02/2024")]
    [InlineData("29/02/2023")]
    [InlineData("2024-13-01")]
    [InlineData("00/01/2024")]
    public void Impossible_dates_are_refused(string raw)
    {
        LegacyCleansing.TryParseDate(raw, Rules, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("30th November 2024")]
    [InlineData("2024/11/30")]
    [InlineData("11/30/2024")]
    [InlineData("yesterday")]
    public void Dates_in_no_mapping_format_are_refused(string raw)
    {
        LegacyCleansing.TryParseDate(raw, Rules, out _).ShouldBeFalse();
    }

    [Fact]
    public void Date_formats_are_tried_in_mapping_order()
    {
        var monthFirst = Rules with { DateFormats = ["MM/dd/yyyy", "dd/MM/yyyy"] };

        LegacyCleansing.TryParseDate("01/02/2024", monthFirst, out var date).ShouldBeTrue();

        date.ShouldBe(new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Unspecified));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("N/A")]
    [InlineData("n/a")]
    [InlineData(" NULL ")]
    public void A_blank_or_null_token_date_is_no_date(string? raw)
    {
        LegacyCleansing.TryParseDate(raw, Rules, out var date).ShouldBeTrue();

        date.ShouldBeNull();
    }

    [Theory]
    [InlineData("1234.56", "1234.56")]
    [InlineData("0", "0")]
    [InlineData(".5", "0.5")]
    [InlineData("1,234.56", "1234.56")]
    [InlineData("1,234,567.89", "1234567.89")]
    [InlineData("1 234 567.89", "1234567.89")]
    [InlineData("1\u00A0234.50", "1234.50")]
    [InlineData("123,456", "123456")]
    public void Numbers_are_read_with_or_without_thousand_separators(string raw, string expected)
    {
        ReadsAs(raw, expected);
    }

    [Theory]
    [InlineData("VLD 1,234.50", "1234.50")]
    [InlineData("1,234.50 VLD", "1234.50")]
    [InlineData("vld1234", "1234")]
    [InlineData("VLD (1,234.50)", "-1234.50")]
    public void A_currency_code_before_or_after_an_amount_is_dropped(string raw, string expected)
    {
        ReadsAs(raw, expected);
    }

    [Theory]
    [InlineData("12.5%", "12.5")]
    [InlineData("12.5 %", "12.5")]
    [InlineData("(12.5)%", "-12.5")]
    public void A_trailing_percent_sign_is_dropped_without_scaling(string raw, string expected)
    {
        ReadsAs(raw, expected);
    }

    [Theory]
    [InlineData("(1,234.50)", "-1234.50")]
    [InlineData("( 12 )", "-12")]
    [InlineData("-5", "-5")]
    [InlineData("+5", "5")]
    [InlineData("- 5", "-5")]
    [InlineData("-1,234.5", "-1234.5")]
    public void Brackets_and_a_leading_sign_give_the_sign(string raw, string expected)
    {
        ReadsAs(raw, expected);
    }

    [Theory]
    [InlineData("  42  ", "42")]
    [InlineData("\t42.10 ", "42.10")]
    public void Surrounding_spaces_are_ignored(string raw, string expected)
    {
        ReadsAs(raw, expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("N/A")]
    [InlineData("n.a.")]
    [InlineData("#N/A")]
    [InlineData(" na ")]
    [InlineData("null")]
    public void A_blank_or_null_token_is_no_value(string? raw)
    {
        LegacyCleansing.TryParseNumber(raw, Rules, out var value).ShouldBeTrue();

        value.ShouldBeNull();
    }

    [Fact]
    public void Null_tokens_match_ignoring_case_and_spaces_around_them()
    {
        var rules = Rules with { NullTokens = ["  Not Available "] };

        LegacyCleansing.IsNull("not available", rules).ShouldBeTrue();
        LegacyCleansing.IsNull("Not", rules).ShouldBeFalse();
    }

    [Theory]
    [InlineData("1.234,56")]
    [InlineData("1.234.567")]
    [InlineData("12,5")]
    [InlineData("1,23,456")]
    [InlineData("1,2345")]
    [InlineData("1,234 567")]
    public void Separators_that_would_need_guessing_are_refused(string raw)
    {
        LegacyCleansing.TryParseNumber(raw, Rules, out var value).ShouldBeFalse();

        value.ShouldBeNull();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12abc")]
    [InlineData("1e5")]
    [InlineData("$100")]
    [InlineData("EUR 100")]
    [InlineData("--5")]
    [InlineData("5-")]
    [InlineData("(5")]
    [InlineData("()")]
    [InlineData("-")]
    [InlineData("%")]
    [InlineData("VLD")]
    [InlineData("1.2.3")]
    public void Anything_else_is_not_a_number(string raw)
    {
        LegacyCleansing.TryParseNumber(raw, Rules, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_number_one_above_the_largest_decimal_is_refused_with_or_without_separators()
    {
        // decimal.MaxValue is 79,228,162,514,264,337,593,543,950,335.
        LegacyCleansing.TryParseNumber("79228162514264337593543950336", Rules, out _).ShouldBeFalse();
        LegacyCleansing.TryParseNumber("(79,228,162,514,264,337,593,543,950,336)", Rules, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("Harbourline Bank PLC")]
    [InlineData("HARBOURLINE BANK PLC")]
    [InlineData("Harbourline Bank Plc.")]
    [InlineData("  harbourline   bank plc ")]
    [InlineData("Harbourline-Bank, PLC")]
    [InlineData("(Harbourline) Bank\u00A0PLC")]
    public void Bank_names_are_cleansed_to_upper_case_words(string name)
    {
        LegacyCleansing.NormalizeName(name).ShouldBe("HARBOURLINE BANK PLC");
    }

    [Theory]
    [InlineData("Crestmont Comm. Bank", "CRESTMONT COMM BANK")]
    [InlineData("Meridian Development Bank (MDB)", "MERIDIAN DEVELOPMENT BANK MDB")]
    [InlineData("T.N.", "T N")]
    [InlineData("Bänk Øst", "BÄNK ØST")]
    public void Punctuation_inside_a_name_separates_words(string name, string expected)
    {
        LegacyCleansing.NormalizeName(name).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    public void A_name_without_letters_or_digits_is_empty(string? name)
    {
        LegacyCleansing.NormalizeName(name).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("1234.5")]
    [InlineData("1234.50")]
    [InlineData("-0.001")]
    [InlineData("0")]
    [InlineData("79228162514264337593543950335")]
    public void Format_writes_invariant_numbers_that_read_back_unchanged(string text)
    {
        var number = decimal.Parse(text, CultureInfo.InvariantCulture);

        var formatted = LegacyCleansing.Format(number);

        formatted.ShouldBe(text);
        LegacyCleansing.TryParseNumber(formatted, Rules, out var back).ShouldBeTrue();
        back.ShouldBe(number);
    }

    [Fact]
    public void Format_writes_no_value_as_null()
    {
        LegacyCleansing.Format(null).ShouldBeNull();
    }

    private static void ReadsDateAs(string raw, string expected)
    {
        LegacyCleansing.TryParseDate(raw, Rules, out var date).ShouldBeTrue();

        date.ShouldBe(DateTime.Parse(expected, CultureInfo.InvariantCulture));
    }

    private static void ReadsAs(string raw, string expected)
    {
        LegacyCleansing.TryParseNumber(raw, Rules, out var value).ShouldBeTrue();

        value.ShouldBe(decimal.Parse(expected, CultureInfo.InvariantCulture));
    }
}
