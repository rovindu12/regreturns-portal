using System.IO.Compression;
using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging;

using RegReturns.Application.Returns;
using RegReturns.Domain.Common;
using RegReturns.Domain.Submissions;

namespace RegReturns.Infrastructure.Files;

/// <summary>
/// Checks and reads uploaded return files in memory (plan §6): the extension must be <c>.xlsx</c> or <c>.csv</c>, the
/// size must be within <see cref="StoredFile.MaxSizeBytes"/>, the content must match the extension, workbooks must not
/// carry macros or exceed the archive limits, and the values come from a <c>FieldCode</c>/<c>Value</c> table read as
/// text. Nothing is written to disk and no formula is evaluated.
/// </summary>
public sealed partial class ReturnFileReader : IReturnFileReader
{
    private readonly ReturnFileLimits _limits;
    private readonly ILogger<ReturnFileReader> _logger;

    /// <summary>Initializes a new instance of the <see cref="ReturnFileReader"/> class with the production limits.</summary>
    /// <param name="logger">The logger.</param>
    public ReturnFileReader(ILogger<ReturnFileReader> logger)
        : this(ReturnFileLimits.Default, logger)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ReturnFileReader"/> class.</summary>
    /// <param name="limits">The limits.</param>
    /// <param name="logger">The logger.</param>
    internal ReturnFileReader(ReturnFileLimits limits, ILogger<ReturnFileReader> logger)
    {
        _limits = limits;
        _logger = logger;
    }

    /// <inheritdoc />
    public Result<ReturnFileContent> Read(string fileName, ReadOnlyMemory<byte> content)
    {
        var format = ReturnFileFormats.FromFileName(fileName);
        if (format is null)
        {
            return UploadErrors.FileType;
        }

        if (content.IsEmpty)
        {
            return UploadErrors.Empty;
        }

        if (content.Length > StoredFile.MaxSizeBytes)
        {
            return UploadErrors.TooLarge;
        }

        return format == ReturnFileFormat.Xlsx ? ReadXlsx(content) : CsvTableReader.Read(content.Span, _limits);
    }

    private Result<ReturnFileContent> ReadXlsx(ReadOnlyMemory<byte> content)
    {
        if (!content.Span.StartsWith(XlsxPackageInspector.ZipLocalFileHeader))
        {
            return UploadErrors.ContentMismatch;
        }

        try
        {
            if (Inspect(content) is { } refused)
            {
                return refused;
            }

            using var workbook = OpenStream(content);
            return XlsxTableReader.Read(workbook, _limits);
        }
        catch (Exception ex)
        {
            // Hostile or broken packages can make the ZIP, XML or ClosedXML code throw almost anything. The maker gets a
            // generic message; the log gets the exception type only, as messages can quote cell content.
            LogWorkbookUnreadable(_logger, ex.GetType().FullName ?? ex.GetType().Name);
            return UploadErrors.Unreadable;
        }
    }

    private Error? Inspect(ReadOnlyMemory<byte> content)
    {
        using var package = OpenStream(content);
        using var archive = new ZipArchive(package, ZipArchiveMode.Read);
        return XlsxPackageInspector.Inspect(archive, content.Length, _limits);
    }

    private static MemoryStream OpenStream(ReadOnlyMemory<byte> content) =>
        MemoryMarshal.TryGetArray(content, out var segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(content.ToArray(), writable: false);

    [LoggerMessage(EventId = 5210, Level = LogLevel.Warning, Message = "Uploaded workbook could not be read ({ExceptionType})")]
    private static partial void LogWorkbookUnreadable(ILogger logger, string exceptionType);
}
