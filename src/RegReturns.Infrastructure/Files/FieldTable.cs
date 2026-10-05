using RegReturns.Application.Returns;
using RegReturns.Domain.Common;

namespace RegReturns.Infrastructure.Files;

/// <summary>Where the code and value columns are, as found in the header row.</summary>
/// <param name="Code">The position of the <see cref="ReturnFileFormats.FieldCodeColumn"/> column.</param>
/// <param name="Value">The position of the <see cref="ReturnFileFormats.ValueColumn"/> column.</param>
internal readonly record struct HeaderColumns(int Code, int Value);

/// <summary>
/// The row rules both formats share: rows without a code are skipped, a code may appear once, rows and cells are
/// capped, and values keep file order. Codes are kept exactly as trimmed, so a lower-case code is reported as unknown.
/// </summary>
/// <param name="limits">The limits.</param>
internal sealed class FieldTable(ReturnFileLimits limits)
{
    private readonly List<KeyValuePair<string, string?>> _values = [];
    private readonly HashSet<string> _codes = new(StringComparer.Ordinal);
    private int _rows;

    /// <summary>Finds the code and value columns in a candidate header row (trimmed, any case, any order).</summary>
    /// <param name="cells">The row's cells as (position, text).</param>
    /// <returns>The columns, or <see langword="null"/> when the row is not the header.</returns>
    public static HeaderColumns? MatchHeader(IEnumerable<(int Column, string? Text)> cells)
    {
        int? code = null;
        int? value = null;
        foreach (var (column, text) in cells)
        {
            var name = text?.Trim();
            if (code is null && string.Equals(name, ReturnFileFormats.FieldCodeColumn, StringComparison.OrdinalIgnoreCase))
            {
                code = column;
            }
            else if (value is null && string.Equals(name, ReturnFileFormats.ValueColumn, StringComparison.OrdinalIgnoreCase))
            {
                value = column;
            }
        }

        return code is { } c && value is { } v ? new HeaderColumns(c, v) : null;
    }

    /// <summary>Trims a cell's text; blank text becomes <see langword="null"/>.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The trimmed text, or <see langword="null"/>.</returns>
    public static string? Normalize(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>Adds a row below the header.</summary>
    /// <param name="code">The normalized code (<see langword="null"/> skips the row).</param>
    /// <param name="value">The normalized value.</param>
    /// <returns><see langword="null"/>, or the reason the file is refused.</returns>
    public Error? Add(string? code, string? value)
    {
        _rows++;
        if (_rows > limits.MaxDataRows)
        {
            return ReadErrors.TooManyRows(limits);
        }

        if (code is null)
        {
            return null;
        }

        if (code.Length > limits.MaxCellLength || value?.Length > limits.MaxCellLength)
        {
            return ReadErrors.CellTooLong(limits);
        }

        if (!_codes.Add(code))
        {
            return UploadErrors.DuplicateField.WithMessage($"The file lists the field {code} more than once.");
        }

        _values.Add(new KeyValuePair<string, string?>(code, value));
        return null;
    }

    /// <summary>Returns what the file contained.</summary>
    /// <param name="format">The format the signature check identified.</param>
    /// <returns>The values in file order, or <see cref="UploadErrors.NoValues"/>.</returns>
    public Result<ReturnFileContent> ToContent(ReturnFileFormat format) =>
        _values.Count == 0 ? UploadErrors.NoValues : new ReturnFileContent(format, _values.AsReadOnly());
}
