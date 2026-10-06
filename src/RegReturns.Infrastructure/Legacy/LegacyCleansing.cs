using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RegReturns.Infrastructure.Legacy;

/// <summary>The value cleansing rules a mapping sets.</summary>
/// <param name="DateFormats">Date formats to try, in order.</param>
/// <param name="ExcelSerialDates">Whether five-digit numbers in date columns are Excel serial dates.</param>
/// <param name="NullTokens">Tokens that mean "no value", matched ignoring case.</param>
/// <param name="CurrencyCodes">Currency codes that may prefix or follow an amount.</param>
internal sealed record CleansingRules(
    IReadOnlyList<string> DateFormats, bool ExcelSerialDates, IReadOnlyList<string> NullTokens, IReadOnlyList<string> CurrencyCodes);

/// <summary>
/// Pure cleansing functions for legacy values (ADR 0029). They accept what legacy exports really contain (thousand
/// separators, currency codes, percent signs, accounting brackets, many date formats, Excel serial dates, "N/A")
/// and refuse anything they would have to guess, such as a comma used as the decimal point.
/// </summary>
internal static partial class LegacyCleansing
{
    private static readonly DateTime ExcelEpoch = new(1899, 12, 30, 0, 0, 0, DateTimeKind.Unspecified);

    /// <summary>
    /// Cleanses a bank name for matching: upper case, every run of characters other than letters and digits becomes
    /// one space, trimmed. <c>Harbourline Bank Plc.</c> and <c>HARBOURLINE BANK PLC</c> both become <c>HARBOURLINE BANK PLC</c>.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The cleansed name, empty for a blank one.</returns>
    public static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(name.Length);
        var pendingSpace = false;
        foreach (var c in name.Trim())
        {
            if (char.IsLetterOrDigit(c))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(char.ToUpperInvariant(c));
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }

        return builder.ToString();
    }

    /// <summary>Returns whether a value is blank or one of the rules' null tokens.</summary>
    /// <param name="raw">The raw value.</param>
    /// <param name="rules">The cleansing rules.</param>
    /// <returns><see langword="true"/> if the value means "no value".</returns>
    public static bool IsNull(string? raw, CleansingRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var text = Squeeze(raw);
        return text.Length == 0 || rules.NullTokens.Any(t => string.Equals(t.Trim(), text, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Reads a number. Accepts surrounding spaces, a currency code before or after, a trailing percent sign, a leading
    /// sign or accounting brackets for negatives, and thousand separators (commas or spaces) in groups of three before
    /// a decimal point. A blank value or null token is no value.
    /// </summary>
    /// <param name="raw">The raw value.</param>
    /// <param name="rules">The cleansing rules.</param>
    /// <param name="value">The number, or <see langword="null"/> for no value.</param>
    /// <returns><see langword="false"/> if the text is not a number this function can read without guessing.</returns>
    public static bool TryParseNumber(string? raw, CleansingRules rules, out decimal? value)
    {
        value = null;
        if (IsNull(raw, rules))
        {
            return true;
        }

        var text = Squeeze(raw);
        foreach (var code in rules.CurrencyCodes.Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            text = StripAffix(text, code.Trim());
        }

        if (text.EndsWith('%'))
        {
            text = text[..^1].TrimEnd();
        }

        var negative = false;
        if (text.Length > 2 && text[0] == '(' && text[^1] == ')')
        {
            (negative, text) = (true, text[1..^1].Trim());
        }

        if (text.Length > 1 && text[0] is '-' or '+')
        {
            negative ^= text[0] == '-';
            text = text[1..].TrimStart();
        }

        if (!PlainNumber().IsMatch(text) && !GroupedNumber().IsMatch(text))
        {
            return false;
        }

        var digits = text.Replace(",", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
        if (!decimal.TryParse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        value = negative ? -number : number;
        return true;
    }

    /// <summary>
    /// Reads a date, with or without a time, trying the rules' formats in order (invariant culture, month names in any
    /// case) and then, if the rules allow it, a five-digit Excel serial date. A blank value or null token is no value.
    /// </summary>
    /// <param name="raw">The raw value.</param>
    /// <param name="rules">The cleansing rules.</param>
    /// <param name="value">The date and time, or <see langword="null"/> for no value.</param>
    /// <returns><see langword="false"/> if no format matches.</returns>
    public static bool TryParseDate(string? raw, CleansingRules rules, out DateTime? value)
    {
        value = null;
        if (IsNull(raw, rules))
        {
            return true;
        }

        var text = Squeeze(raw);
        foreach (var format in rules.DateFormats)
        {
            if (DateTime.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                value = parsed;
                return true;
            }
        }

        if (rules.ExcelSerialDates && ExcelSerial().IsMatch(text)
            && double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var serial))
        {
            value = ExcelEpoch.AddDays(serial);
            return true;
        }

        return false;
    }

    /// <summary>Formats a number the way the template parser reads it: invariant culture, no separators.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The text, or <see langword="null"/> for no value.</returns>
    public static string? Format(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);

    // Trims and turns no-break, thin and other Unicode spaces into plain spaces, collapsing runs of them.
    private static string Squeeze(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        return Spaces().Replace(raw.Trim(), " ");
    }

    private static string StripAffix(string text, string code)
    {
        if (text.StartsWith(code, StringComparison.OrdinalIgnoreCase))
        {
            return text[code.Length..].TrimStart();
        }

        return text.EndsWith(code, StringComparison.OrdinalIgnoreCase) ? text[..^code.Length].TrimEnd() : text;
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Spaces();

    [GeneratedRegex(@"^(\d+(\.\d+)?|\.\d+)\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex PlainNumber();

    // 1,234,567.89 or 1 234 567.89: one separator kind, groups of three, optional decimals.
    [GeneratedRegex(@"^\d{1,3}((,\d{3})+|( \d{3})+)(\.\d+)?\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex GroupedNumber();

    [GeneratedRegex(@"^\d{5}(\.\d+)?\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex ExcelSerial();
}
