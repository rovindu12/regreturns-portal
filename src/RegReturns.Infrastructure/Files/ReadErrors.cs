using System.Globalization;

using RegReturns.Application.Returns;
using RegReturns.Domain.Common;

namespace RegReturns.Infrastructure.Files;

/// <summary>
/// Specific messages for <see cref="UploadErrors.Unreadable"/>. The code stays the same; the messages are short, are
/// shown to the maker and never carry exception text or file content.
/// </summary>
internal static class ReadErrors
{
    /// <summary>Gets the error for a workbook over the archive limits (a possible zip bomb).</summary>
    public static Error TooComplex { get; } =
        UploadErrors.Unreadable.WithMessage("The workbook is too large or too complex to read safely.");

    /// <summary>Gets the error for a number that cannot be a return figure.</summary>
    public static Error NumberOutOfRange { get; } =
        UploadErrors.Unreadable.WithMessage("The file has a number too large to read.");

    /// <summary>Returns the error for too many rows below the header.</summary>
    /// <param name="limits">The limits.</param>
    /// <returns>The error.</returns>
    public static Error TooManyRows(ReturnFileLimits limits) => UploadErrors.Unreadable.WithMessage(
        string.Create(CultureInfo.InvariantCulture, $"The file has more than {limits.MaxDataRows:N0} rows below the header."));

    /// <summary>Returns the error for a cell or field over the length limit.</summary>
    /// <param name="limits">The limits.</param>
    /// <returns>The error.</returns>
    public static Error CellTooLong(ReturnFileLimits limits) => UploadErrors.Unreadable.WithMessage(
        string.Create(CultureInfo.InvariantCulture, $"The file has a cell longer than {limits.MaxCellLength:N0} characters."));

    /// <summary>Returns the error for a CSV line with too many fields.</summary>
    /// <param name="line">The line the record starts on.</param>
    /// <param name="limits">The limits.</param>
    /// <returns>The error.</returns>
    public static Error TooManyColumns(int line, ReturnFileLimits limits) => UploadErrors.Unreadable.WithMessage(
        string.Create(CultureInfo.InvariantCulture, $"Line {line} of the file has more than {limits.MaxCsvColumns:N0} columns."));

    /// <summary>Returns the error for text after a closing quote.</summary>
    /// <param name="line">The line.</param>
    /// <returns>The error.</returns>
    public static Error NotCsv(int line) => UploadErrors.Unreadable.WithMessage(
        string.Create(CultureInfo.InvariantCulture, $"Line {line} of the file is not valid CSV: a quoted value must end at a comma or the end of the line."));

    /// <summary>Returns the error for a quoted value that is never closed.</summary>
    /// <param name="line">The line the value starts on.</param>
    /// <returns>The error.</returns>
    public static Error UnclosedQuote(int line) => UploadErrors.Unreadable.WithMessage(
        string.Create(CultureInfo.InvariantCulture, $"The quoted value starting on line {line} of the file is never closed."));
}
