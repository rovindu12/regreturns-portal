using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace RegReturns.Domain.Common;

/// <summary>
/// Argument checks used by entity factories. Failures throw <see cref="DomainException"/>.
/// </summary>
internal static partial class Guard
{
    /// <summary>Ensures a string is not null or whitespace and not longer than <paramref name="maxLength"/>.</summary>
    public static string NotBlank(string? value, int maxLength, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{name} is required.");
        }

        var trimmed = value.Trim();
        return trimmed.Length > maxLength
            ? throw new DomainException($"{name} must be at most {maxLength} characters.")
            : trimmed;
    }

    /// <summary>Ensures a code is upper-case letters, digits and underscores, starting with a letter.</summary>
    public static string Code(string? value, int maxLength, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        var code = NotBlank(value, maxLength, name);
        return CodePattern().IsMatch(code)
            ? code
            : throw new DomainException($"{name} '{code}' must contain only A-Z, 0-9 and '_' and start with a letter.");
    }

    /// <summary>Returns whether a value is a valid code: A-Z, 0-9 and '_', starting with a letter, at most <paramref name="maxLength"/>.</summary>
    public static bool IsCode(string? value, int maxLength) =>
        value is not null && value.Length <= maxLength && CodePattern().IsMatch(value);

    /// <summary>Ensures a Guid is not empty.</summary>
    public static Guid NotEmpty(Guid value, [CallerArgumentExpression(nameof(value))] string? name = null) =>
        value == Guid.Empty ? throw new DomainException($"{name} is required.") : value;

    [GeneratedRegex(@"^[A-Z][A-Z0-9_]*\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex CodePattern();
}
