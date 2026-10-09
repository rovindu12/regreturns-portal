using System.Buffers;
using System.Text;

namespace RegReturns.Infrastructure.Files;

/// <summary>Writes CSV lines (RFC 4180, CRLF) with every cell guarded against spreadsheet formula injection.</summary>
internal static class CsvText
{
    private const string LineBreak = "\r\n";
    private const char Separator = ',';
    private const string Quote = "\"";
    private const string EscapedQuote = "\"\"";

    private static readonly SearchValues<char> Specials = SearchValues.Create(",\"\r\n");

    /// <summary>Gets the UTF-8 byte order mark, which makes Excel read the file as UTF-8.</summary>
    public static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    /// <summary>Appends one line, quoting cells that need it and prefixing cells that look like formulas.</summary>
    /// <param name="builder">The text so far.</param>
    /// <param name="cells">The cells; <see langword="null"/> is an empty cell.</param>
    public static void AppendLine(StringBuilder builder, IReadOnlyList<string?> cells)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(cells);
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(Separator);
            }

            var text = CsvFormulaGuard.Protect(cells[i] ?? string.Empty);
            builder.Append(text.AsSpan().ContainsAny(Specials)
                ? Quote + text.Replace(Quote, EscapedQuote, StringComparison.Ordinal) + Quote
                : text);
        }

        builder.Append(LineBreak);
    }

    /// <summary>Encodes CSV text as UTF-8 with a byte order mark.</summary>
    /// <param name="text">The CSV text.</param>
    /// <returns>The bytes.</returns>
    public static byte[] ToUtf8WithBom(string text) => [.. Utf8Bom, .. Encoding.UTF8.GetBytes(text)];
}
