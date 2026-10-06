namespace RegReturns.Migrator.Legacy;

/// <summary>A plain-text table for the console: columns padded to their widest cell, numbers aligned right.</summary>
/// <param name="headers">The column headers; a header starting with '>' marks a right-aligned column (the '>' is not shown).</param>
internal sealed class ConsoleTable(params string[] headers)
{
    private readonly List<string[]> _rows = [];

    /// <summary>Adds a row.</summary>
    /// <param name="cells">One cell per column.</param>
    public void Add(params string[] cells)
    {
        if (cells.Length != headers.Length)
        {
            throw new ArgumentException($"A row needs {headers.Length} cells.", nameof(cells));
        }

        _rows.Add(cells);
    }

    /// <summary>Writes the table, indented by two spaces.</summary>
    /// <param name="output">Where to write.</param>
    public void WriteTo(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var right = headers.Select(h => h.StartsWith('>')).ToArray();
        var titles = headers.Select(h => h.TrimStart('>')).ToArray();
        var widths = titles.Select((t, i) => _rows.Select(r => r[i].Length).Append(t.Length).Max()).ToArray();
        WriteRow(output, titles, widths, right);
        foreach (var row in _rows)
        {
            WriteRow(output, row, widths, right);
        }
    }

    private static void WriteRow(TextWriter output, string[] cells, int[] widths, bool[] right)
    {
        var padded = cells.Select((c, i) => right[i] ? c.PadLeft(widths[i]) : c.PadRight(widths[i]));
        output.WriteLine(("  " + string.Join("  ", padded)).TrimEnd());
    }
}
