using System.Buffers;
using System.Text;

using RegReturns.Domain.Common;

namespace RegReturns.Infrastructure.Files;

/// <summary>
/// An RFC 4180 reader: comma-separated fields, double-quoted fields that may hold commas, line breaks and doubled
/// quotes, and CRLF, LF or CR line endings. A quote inside an unquoted field is kept as text. Field length and the
/// number of fields per record are capped.
/// </summary>
/// <param name="text">The decoded file content, without a byte order mark.</param>
/// <param name="limits">The limits.</param>
internal sealed class CsvParser(string text, ReturnFileLimits limits)
{
    private const char Separator = ',';
    private const char Quote = '"';
    private const char CarriageReturn = '\r';
    private const char LineFeed = '\n';

    private static readonly SearchValues<char> UnquotedFieldEnd = SearchValues.Create(",\r\n");

    private int _position;
    private int _line = 1;

    /// <summary>Gets the line the next record starts on, counting from 1.</summary>
    public int Line => _line;

    /// <summary>Gets a value indicating whether every record has been read.</summary>
    public bool AtEnd => _position >= text.Length;

    /// <summary>Reads the next record. Call only while <see cref="AtEnd"/> is <see langword="false"/>.</summary>
    /// <returns>The record's fields, or why the file cannot be read.</returns>
    public Result<IReadOnlyList<string>> ReadRecord()
    {
        var line = _line;
        var fields = new List<string>();
        do
        {
            var field = _position < text.Length && text[_position] == Quote ? ReadQuoted() : ReadUnquoted();
            if (field.IsFailure)
            {
                return field.Error!;
            }

            fields.Add(field.Value);
            if (fields.Count > limits.MaxCsvColumns)
            {
                return ReadErrors.TooManyColumns(line, limits);
            }
        }
        while (SkipSeparator());

        SkipLineBreak();
        return Result.Success<IReadOnlyList<string>>(fields);
    }

    private Result<string> ReadUnquoted()
    {
        var rest = text.AsSpan(_position);
        var length = rest.IndexOfAny(UnquotedFieldEnd);
        if (length < 0)
        {
            length = rest.Length;
        }

        if (length > limits.MaxCellLength)
        {
            return ReadErrors.CellTooLong(limits);
        }

        var field = text.Substring(_position, length);
        _position += length;
        return field;
    }

    private Result<string> ReadQuoted()
    {
        var startLine = _line;
        _position++;
        var field = new StringBuilder();
        var closing = text.IndexOf(Quote, _position);
        while (closing >= 0)
        {
            var chunk = text.AsSpan(_position, closing - _position);
            if (field.Length + chunk.Length > limits.MaxCellLength)
            {
                return ReadErrors.CellTooLong(limits);
            }

            field.Append(chunk);
            _line += CountLineBreaks(chunk);
            _position = closing + 1;
            if (_position >= text.Length || text[_position] != Quote)
            {
                return AtFieldEnd() ? field.ToString() : ReadErrors.NotCsv(_line);
            }

            // A doubled quote is one quote character inside the value.
            field.Append(Quote);
            _position++;
            closing = text.IndexOf(Quote, _position);
        }

        return ReadErrors.UnclosedQuote(startLine);
    }

    private bool AtFieldEnd() => _position >= text.Length || text[_position] is Separator or CarriageReturn or LineFeed;

    private bool SkipSeparator()
    {
        if (_position < text.Length && text[_position] == Separator)
        {
            _position++;
            return true;
        }

        return false;
    }

    private void SkipLineBreak()
    {
        if (_position < text.Length && text[_position] == CarriageReturn)
        {
            _position++;
        }

        if (_position < text.Length && text[_position] == LineFeed)
        {
            _position++;
        }

        _line++;
    }

    private static int CountLineBreaks(ReadOnlySpan<char> chunk)
    {
        // CRLF counts once (at its LF); a CR on its own counts too.
        var count = 0;
        for (var i = 0; i < chunk.Length; i++)
        {
            if (chunk[i] == LineFeed || (chunk[i] == CarriageReturn && (i + 1 == chunk.Length || chunk[i + 1] != LineFeed)))
            {
                count++;
            }
        }

        return count;
    }
}
