using System.Text;
using System.Text.Unicode;

using RegReturns.Application.Returns;
using RegReturns.Domain.Common;

namespace RegReturns.Infrastructure.Files;

/// <summary>
/// Reads an uploaded <c>.csv</c> file: strict UTF-8 (optional byte order mark) with no NUL bytes, then a
/// <c>FieldCode</c>/<c>Value</c> table under a header row found in the first rows.
/// </summary>
internal static class CsvTableReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Gets the UTF-8 byte order mark.</summary>
    internal static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    // Binary formats whose first bytes are printable, so the text checks alone might not catch them.
    private static ReadOnlySpan<byte> PdfSignature => "%PDF-"u8;

    /// <summary>Checks and reads the file.</summary>
    /// <param name="content">The file content.</param>
    /// <param name="limits">The limits.</param>
    /// <returns>The values, or why the file is refused.</returns>
    public static Result<ReturnFileContent> Read(ReadOnlySpan<byte> content, ReturnFileLimits limits)
    {
        var body = content.StartsWith(Utf8Bom) ? content[Utf8Bom.Length..] : content;
        if (!IsText(body))
        {
            return UploadErrors.ContentMismatch;
        }

        var parser = new CsvParser(StrictUtf8.GetString(body), limits);
        var header = FindHeader(parser, limits);
        if (header.IsFailure)
        {
            return header.Error!;
        }

        var columns = header.Value;
        var table = new FieldTable(limits);
        while (!parser.AtEnd)
        {
            var record = parser.ReadRecord();
            if (record.IsFailure)
            {
                return record.Error!;
            }

            var code = FieldTable.Normalize(FieldAt(record.Value, columns.Code));
            var value = CsvFormulaGuard.Unprotect(FieldTable.Normalize(FieldAt(record.Value, columns.Value)));
            if (table.Add(code, value) is { } refused)
            {
                return refused;
            }
        }

        return table.ToContent(ReturnFileFormat.Csv);
    }

    /// <summary>Returns whether the bytes are UTF-8 text with no NUL bytes and no well-known binary signature.</summary>
    /// <param name="body">The content after any byte order mark.</param>
    /// <returns><see langword="true"/> for text.</returns>
    internal static bool IsText(ReadOnlySpan<byte> body) =>
        !body.StartsWith(XlsxPackageInspector.ZipLocalFileHeader)
        && !body.StartsWith(PdfSignature)
        && !body.Contains((byte)0)
        && Utf8.IsValid(body);

    private static Result<HeaderColumns> FindHeader(CsvParser parser, ReturnFileLimits limits)
    {
        for (var row = 0; row < limits.HeaderSearchRows && !parser.AtEnd; row++)
        {
            var record = parser.ReadRecord();
            if (record.IsFailure)
            {
                return record.Error!;
            }

            if (FieldTable.MatchHeader(record.Value.Select((text, column) => (column, (string?)text))) is { } header)
            {
                return header;
            }
        }

        return UploadErrors.NoHeader;
    }

    private static string? FieldAt(IReadOnlyList<string> record, int column) => column < record.Count ? record[column] : null;
}
