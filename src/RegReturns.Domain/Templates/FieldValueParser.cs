using System.Globalization;
using System.Text.RegularExpressions;

namespace RegReturns.Domain.Templates;

/// <summary>Whether a raw field value is blank, valid for the field's data type, or invalid.</summary>
public enum FieldValueState
{
    /// <summary>No value was entered.</summary>
    Blank = 1,

    /// <summary>The value parses as the field's data type and precision.</summary>
    Valid = 2,

    /// <summary>The value does not parse as the field's data type, or has too many decimal places.</summary>
    Invalid = 3,
}

/// <summary>The outcome of parsing one raw field value.</summary>
/// <param name="State">Blank, valid or invalid.</param>
/// <param name="Number">The number, for valid values of numeric fields; otherwise <see langword="null"/>.</param>
public readonly record struct ParsedFieldValue(FieldValueState State, decimal? Number)
{
    /// <summary>Gets a blank value.</summary>
    public static ParsedFieldValue Blank { get; } = new(FieldValueState.Blank, null);

    /// <summary>Gets an invalid value.</summary>
    public static ParsedFieldValue Invalid { get; } = new(FieldValueState.Invalid, null);
}

/// <summary>
/// Parses raw field values by data type, the same way for web forms, uploads, the API and the migrator.
/// Numbers use the invariant culture: <c>.</c> for decimals and optional <c>,</c> between groups of three digits
/// (<c>1,234.50</c>). Percentages may end in <c>%</c>. Dates are <c>yyyy-MM-dd</c>; booleans are true/false or yes/no.
/// </summary>
public static partial class FieldValueParser
{
    /// <summary>The ISO 8601 date format accepted for <see cref="FieldDataType.Date"/> fields.</summary>
    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>
    /// The largest magnitude a number may have: what the <c>decimal(19,4)</c> value columns hold.
    /// </summary>
    public const decimal MaxMagnitude = 999_999_999_999_999.9999m;

    /// <summary>Parses a raw value for a field.</summary>
    /// <param name="field">The template field.</param>
    /// <param name="raw">The raw value as entered or uploaded.</param>
    /// <returns>Blank, valid (with the number for numeric fields) or invalid.</returns>
    public static ParsedFieldValue Parse(TemplateField field, string? raw)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ParsedFieldValue.Blank;
        }

        var text = raw.Trim();
        return field.DataType switch
        {
            FieldDataType.Amount or FieldDataType.WholeNumber => ParseNumber(text, field.Precision),
            FieldDataType.Percentage => ParseNumber(text.EndsWith('%') ? text[..^1].TrimEnd() : text, field.Precision),
            FieldDataType.Date => DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                ? new ParsedFieldValue(FieldValueState.Valid, null)
                : ParsedFieldValue.Invalid,
            FieldDataType.Boolean => TryParseBoolean(text, out _)
                ? new ParsedFieldValue(FieldValueState.Valid, null)
                : ParsedFieldValue.Invalid,
            _ => new ParsedFieldValue(FieldValueState.Valid, null),
        };
    }

    /// <summary>Parses a boolean written as true/false or yes/no, in any case.</summary>
    /// <param name="text">The text.</param>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> if the text is a boolean.</returns>
    public static bool TryParseBoolean(string? text, out bool value)
    {
        var trimmed = text?.Trim();
        value = string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "yes", StringComparison.OrdinalIgnoreCase);
        return value
            || string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "no", StringComparison.OrdinalIgnoreCase);
    }

    private static ParsedFieldValue ParseNumber(string text, int precision)
    {
        // decimal.TryParse with AllowThousands accepts separators anywhere ("1,2,3" is 123), so the shape is checked first.
        if (!NumberPattern().IsMatch(text)
            || !decimal.TryParse(text.Replace(",", string.Empty, StringComparison.Ordinal),
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
            || Math.Abs(number) > MaxMagnitude
            || decimal.Round(number, precision) != number)
        {
            return ParsedFieldValue.Invalid;
        }

        return new ParsedFieldValue(FieldValueState.Valid, number);
    }

    // An optional sign, digits with or without groups of three separated by commas, and optional decimals.
    [GeneratedRegex(@"^[+-]?([0-9]{1,3}(,[0-9]{3})+|[0-9]+)(\.[0-9]+)?\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex NumberPattern();
}
