using System.Security.Cryptography;
using System.Text;

using RegReturns.Application.Migration;
using RegReturns.Domain.Common;
using RegReturns.Infrastructure.Files;

namespace RegReturns.Infrastructure.Legacy;

/// <summary>A data row of a legacy file.</summary>
/// <param name="LineNumber">The line the row starts on, counting the header as line 1.</param>
/// <param name="Cells">The cells as read.</param>
internal sealed record LegacyRow(int LineNumber, IReadOnlyList<string> Cells)
{
    /// <summary>Gets a value indicating whether every cell is blank.</summary>
    public bool IsBlank => Cells.All(string.IsNullOrWhiteSpace);
}

/// <summary>A legacy CSV file: its header, its data rows and the SHA-256 of its bytes.</summary>
/// <param name="FileName">The file name.</param>
/// <param name="Sha256">The lowercase hex SHA-256 of the file's bytes.</param>
/// <param name="Header">The header cells, trimmed.</param>
/// <param name="Rows">The data rows, blank ones included.</param>
internal sealed record LegacyTable(string FileName, string Sha256, IReadOnlyList<string> Header, IReadOnlyList<LegacyRow> Rows);

/// <summary>Reads a legacy CSV file: UTF-8 with or without a byte order mark, comma separated, quoted as RFC 4180.</summary>
internal static class LegacyCsvReader
{
    /// <summary>The largest file the migrator reads into memory.</summary>
    public const int MaxFileBytes = 64 * 1024 * 1024;

    private static readonly ReturnFileLimits Limits = ReturnFileLimits.Default;

    /// <summary>Reads a file's bytes.</summary>
    /// <param name="fileName">The file name, for messages.</param>
    /// <param name="content">The file's bytes.</param>
    /// <returns>The table, or <see cref="MigrationErrors.FileUnreadable"/> saying why.</returns>
    public static Result<LegacyTable> Read(string fileName, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length > MaxFileBytes)
        {
            return Unreadable(fileName, $"it is larger than {MaxFileBytes / (1024 * 1024)} MB");
        }

        var body = content.AsSpan();
        if (body.StartsWith(CsvTableReader.Utf8Bom))
        {
            body = body[CsvTableReader.Utf8Bom.Length..];
        }

        if (!CsvTableReader.IsText(body))
        {
            return Unreadable(fileName, "it is not UTF-8 text");
        }

        var parser = new CsvParser(Encoding.UTF8.GetString(body), Limits);
        if (parser.AtEnd)
        {
            return Unreadable(fileName, "it is empty");
        }

        var header = parser.ReadRecord();
        if (header.IsFailure)
        {
            return Unreadable(fileName, header.Error!.Message);
        }

        var rows = new List<LegacyRow>();
        while (!parser.AtEnd)
        {
            var line = parser.Line;
            var record = parser.ReadRecord();
            if (record.IsFailure)
            {
                return Unreadable(fileName, record.Error!.Message);
            }

            rows.Add(new LegacyRow(line, record.Value));
        }

        return new LegacyTable(
            fileName, Convert.ToHexStringLower(SHA256.HashData(content)), [.. header.Value.Select(h => h.Trim())], rows);
    }

    private static Error Unreadable(string fileName, string reason) =>
        MigrationErrors.FileUnreadable.WithMessage($"{fileName} cannot be read: {reason}.");
}
