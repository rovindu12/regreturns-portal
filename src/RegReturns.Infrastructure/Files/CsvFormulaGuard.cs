using System.Buffers;
using System.Globalization;

namespace RegReturns.Infrastructure.Files;

/// <summary>
/// Neutralises CSV formula injection (OWASP "CSV Injection"): a cell that a spreadsheet could run as a formula
/// (starting with <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, a tab or a carriage return, and not a plain number) is written
/// with a leading apostrophe. Cells that already start with apostrophes before such a character get one more, so
/// <see cref="Unprotect"/> is the exact inverse of <see cref="Protect"/> and a downloaded file uploads unchanged.
/// </summary>
internal static class CsvFormulaGuard
{
    /// <summary>The prefix that makes a spreadsheet treat a cell as text.</summary>
    public const char Prefix = '\'';

    private static readonly SearchValues<char> FormulaStarts = SearchValues.Create("=+-@\t\r");

    /// <summary>Prefixes a cell with an apostrophe when a spreadsheet could run it as a formula.</summary>
    /// <param name="text">The cell text.</param>
    /// <returns>The text to write.</returns>
    public static string Protect(string text) => NeedsPrefix(text) ? Prefix + text : text;

    /// <summary>Removes the apostrophe <see cref="Protect"/> added.</summary>
    /// <param name="text">The cell text as read.</param>
    /// <returns>The original text.</returns>
    public static string? Unprotect(string? text) =>
        text is { Length: > 1 } && text[0] == Prefix && NeedsPrefix(text.AsSpan(1)) ? text[1..] : text;

    private static bool NeedsPrefix(ReadOnlySpan<char> text)
    {
        var rest = text.TrimStart(Prefix);
        return !rest.IsEmpty && FormulaStarts.Contains(rest[0]) && !IsPlainNumber(rest);
    }

    private static bool IsPlainNumber(ReadOnlySpan<char> text) =>
        decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _);
}
