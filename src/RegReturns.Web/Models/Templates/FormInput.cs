using System.Globalization;

using RegReturns.Domain.Common;

namespace RegReturns.Web.Models.Templates;

/// <summary>Errors for form input the server could not read, before any business rule runs.</summary>
public static class FormErrors
{
    /// <summary>A value has the wrong shape, such as a number written with a comma.</summary>
    public static readonly Error InvalidInput = new("Form.InvalidInput", "Some of the values could not be read. Check them and try again.");

    /// <summary>An irreversible action was posted without ticking its confirmation box.</summary>
    public static readonly Error NotConfirmed = new("Form.NotConfirmed", "Tick the box to confirm, then try again.");
}

/// <summary>
/// Reads posted form values. Numbers use the invariant culture with a dot for decimals and no group separators, so
/// <c>1,5</c> is refused rather than read as fifteen; dates are <c>yyyy-MM-dd</c> as date inputs post them.
/// </summary>
public static class FormInput
{
    private const NumberStyles DecimalStyle =
        NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    /// <summary>Reads an optional decimal.</summary>
    /// <param name="text">The posted text.</param>
    /// <param name="name">What the value is, for the error message, such as <c>minimum</c>.</param>
    /// <returns>The number, <see langword="null"/> when blank, or <see cref="FormErrors.InvalidInput"/>.</returns>
    public static Result<decimal?> OptionalDecimal(string? text, string name)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (decimal?)null;
        }

        return decimal.TryParse(text, DecimalStyle, CultureInfo.InvariantCulture, out var number)
            ? number
            : FormErrors.InvalidInput.WithMessage(
                $"The {name} must be a number written with a dot for decimals and no other separators, for example 1500.25.");
    }

    /// <summary>Reads an optional whole number of at least zero.</summary>
    /// <param name="text">The posted text.</param>
    /// <param name="fallback">The value when blank.</param>
    /// <param name="message">The error message when the text is not a whole number.</param>
    /// <returns>The number, or <see cref="FormErrors.InvalidInput"/>.</returns>
    public static Result<int> WholeNumber(string? text, int fallback, string message)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        return int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : FormErrors.InvalidInput.WithMessage(message);
    }

    /// <summary>Reads an optional choice from a select, matching the enum member's name exactly.</summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="text">The posted text.</param>
    /// <param name="message">The error message when the text names no member.</param>
    /// <returns>The member, <see langword="null"/> when blank, or <see cref="FormErrors.InvalidInput"/>.</returns>
    public static Result<TEnum?> OptionalChoice<TEnum>(string? text, string message)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (TEnum?)null;
        }

        // Names only: Enum.TryParse would also take numbers and comma-separated combinations.
        var name = text.Trim();
        return Enum.GetNames<TEnum>().Contains(name, StringComparer.Ordinal)
            ? (TEnum?)Enum.Parse<TEnum>(name)
            : FormErrors.InvalidInput.WithMessage(message);
    }

    /// <summary>Reads a required choice from a select, matching the enum member's name exactly.</summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="text">The posted text.</param>
    /// <param name="message">The error message when the text is blank or names no member.</param>
    /// <returns>The member, or <see cref="FormErrors.InvalidInput"/>.</returns>
    public static Result<TEnum> Choice<TEnum>(string? text, string message)
        where TEnum : struct, Enum
    {
        var choice = OptionalChoice<TEnum>(text, message);
        if (choice.IsFailure)
        {
            return choice.Error!;
        }

        return choice.Value is { } value ? value : FormErrors.InvalidInput.WithMessage(message);
    }

    /// <summary>Reads a required date in the date input format.</summary>
    /// <param name="text">The posted text.</param>
    /// <param name="name">What the date is, for the error message, such as <c>effective-from date</c>.</param>
    /// <returns>The date, or <see cref="FormErrors.InvalidInput"/>.</returns>
    public static Result<DateOnly> Date(string? text, string name) =>
        DateOnly.TryParseExact(text?.Trim(), TemplateDisplay.DateInputFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : FormErrors.InvalidInput.WithMessage($"Enter the {name} as a date, for example 2026-11-01.");

    /// <summary>Trims a code and converts it to upper case, since codes are A-Z, 0-9 and '_'.</summary>
    /// <param name="text">The posted text.</param>
    /// <returns>The code, or an empty string when blank.</returns>
    public static string Code(string? text) => text?.Trim().ToUpperInvariant() ?? string.Empty;
}
