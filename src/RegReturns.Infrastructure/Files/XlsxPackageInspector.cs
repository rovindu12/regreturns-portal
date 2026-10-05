using System.Buffers;
using System.IO.Compression;
using System.Xml;

using RegReturns.Application.Returns;
using RegReturns.Domain.Common;

namespace RegReturns.Infrastructure.Files;

/// <summary>
/// Checks an <c>.xlsx</c> package before ClosedXML opens it: it must be an Open Packaging Conventions ZIP (a
/// <c>[Content_Types].xml</c> entry), must not carry macros, and must stay within the entry count, total size and
/// compression ratio limits. Sizes are checked as declared and again as actually decompressed, so a package that lies
/// about its sizes is caught too.
/// </summary>
internal static class XlsxPackageInspector
{
    /// <summary>The name of the package's content types entry.</summary>
    public const string ContentTypesEntryName = "[Content_Types].xml";

    /// <summary>The file name of the VBA project part of a macro-enabled workbook.</summary>
    public const string VbaProjectFileName = "vbaProject.bin";

    private const string ContentTypeAttribute = "ContentType";
    private const int BufferSize = 81_920;

    // Content types of macro-enabled workbooks, VBA projects and Excel 4.0 macro sheets.
    private static readonly string[] MacroContentTypeMarkers = ["macroEnabled", "vbaProject", "macrosheet"];

    /// <summary>Gets the ZIP local file header signature every <c>.xlsx</c> starts with.</summary>
    public static ReadOnlySpan<byte> ZipLocalFileHeader => [0x50, 0x4B, 0x03, 0x04];

    /// <summary>Inspects an opened package.</summary>
    /// <param name="archive">The package, opened for reading.</param>
    /// <param name="packageLength">The size of the package file in bytes.</param>
    /// <param name="limits">The limits.</param>
    /// <returns><see langword="null"/> when the package may be opened; otherwise why it is refused.</returns>
    public static Error? Inspect(ZipArchive archive, long packageLength, ReturnFileLimits limits)
    {
        var entries = archive.Entries;
        if (entries.Count > limits.MaxArchiveEntries)
        {
            return ReadErrors.TooComplex;
        }

        var contentTypes = entries.FirstOrDefault(
            e => string.Equals(e.FullName, ContentTypesEntryName, StringComparison.OrdinalIgnoreCase));
        if (contentTypes is null)
        {
            return UploadErrors.ContentMismatch;
        }

        if (entries.Any(e => e.FullName.EndsWith(VbaProjectFileName, StringComparison.OrdinalIgnoreCase)))
        {
            return UploadErrors.MacrosNotAllowed;
        }

        if (!DeclaredSizesWithinLimits(entries, packageLength, limits) || !ExpandsWithinLimits(entries, limits))
        {
            return ReadErrors.TooComplex;
        }

        return DeclaresMacros(contentTypes) ? UploadErrors.MacrosNotAllowed : null;
    }

    private static bool DeclaredSizesWithinLimits(IEnumerable<ZipArchiveEntry> entries, long packageLength, ReturnFileLimits limits)
    {
        long total = 0;
        foreach (var entry in entries)
        {
            if (entry.CompressedLength > packageLength || entry.Length > entry.CompressedLength * limits.MaxCompressionRatio)
            {
                return false;
            }

            total += entry.Length;
            if (total > limits.MaxUncompressedBytes)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ExpandsWithinLimits(IEnumerable<ZipArchiveEntry> entries, ReturnFileLimits limits)
    {
        var budget = limits.MaxUncompressedBytes;
        foreach (var entry in entries)
        {
            // Never more than the entry declared (which already passed the ratio check) or the total budget left.
            var allowed = Math.Min(entry.Length, budget);
            var expanded = ExpandedLength(entry, allowed);
            if (expanded > allowed)
            {
                return false;
            }

            budget -= expanded;
        }

        return true;
    }

    private static long ExpandedLength(ZipArchiveEntry entry, long allowed)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            using var stream = entry.Open();
            long total = 0;
            int read;
            do
            {
                read = stream.Read(buffer);
                total += read;
            }
            while (read > 0 && total <= allowed);

            return total;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool DeclaresMacros(ZipArchiveEntry contentTypes)
    {
        // Parsed rather than searched as text, so character references cannot hide a content type.
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
        };
        using var stream = contentTypes.Open();
        using var reader = XmlReader.Create(stream, settings);
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element
                && reader.GetAttribute(ContentTypeAttribute) is { } contentType
                && Array.Exists(MacroContentTypeMarkers, m => contentType.Contains(m, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}
