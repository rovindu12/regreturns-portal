namespace RegReturns.Infrastructure.Files;

/// <summary>
/// Limits that stop a hostile or broken upload from taking unbounded memory or time. Archive limits are checked
/// before ClosedXML sees a workbook; the uploaded file itself is already capped at
/// <see cref="Domain.Submissions.StoredFile.MaxSizeBytes"/>.
/// </summary>
/// <param name="MaxArchiveEntries">The most entries an <c>.xlsx</c> package may have.</param>
/// <param name="MaxUncompressedBytes">The most bytes all entries of an <c>.xlsx</c> package may expand to together.</param>
/// <param name="MaxCompressionRatio">The highest uncompressed-to-compressed size ratio of one entry.</param>
/// <param name="HeaderSearchRows">How many rows from the top are searched for the header row.</param>
/// <param name="MaxDataRows">The most rows below the header, blank ones included.</param>
/// <param name="MaxCellLength">The most characters in one cell or CSV field.</param>
/// <param name="MaxCsvColumns">The most fields on one CSV line.</param>
internal sealed record ReturnFileLimits(
    int MaxArchiveEntries,
    long MaxUncompressedBytes,
    int MaxCompressionRatio,
    int HeaderSearchRows,
    int MaxDataRows,
    int MaxCellLength,
    int MaxCsvColumns)
{
    /// <summary>Gets the limits used in production.</summary>
    public static ReturnFileLimits Default { get; } = new(
        MaxArchiveEntries: 200,
        MaxUncompressedBytes: 50L * 1024 * 1024,
        MaxCompressionRatio: 100,
        HeaderSearchRows: 20,
        MaxDataRows: 2_000,
        MaxCellLength: 4_000,
        MaxCsvColumns: 200);
}
