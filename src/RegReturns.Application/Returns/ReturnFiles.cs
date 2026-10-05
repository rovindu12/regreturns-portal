using RegReturns.Domain.Common;
using RegReturns.Domain.Templates;

namespace RegReturns.Application.Returns;

/// <summary>The file formats a return can be uploaded or downloaded in.</summary>
public enum ReturnFileFormat
{
    /// <summary>An Excel workbook (<c>.xlsx</c>; macro-enabled workbooks are refused).</summary>
    Xlsx = 1,

    /// <summary>UTF-8 comma-separated values (<c>.csv</c>).</summary>
    Csv = 2,
}

/// <summary>
/// Checks and parses uploaded return files (plan §6): extension allow-list, content signature, size and archive limits,
/// no macros, then a <c>FieldCode</c>/<c>Value</c> table read as text. Files are never written to disk or executed.
/// </summary>
public interface IReturnFileReader
{
    /// <summary>Inspects and reads an uploaded file.</summary>
    /// <param name="fileName">The name the browser sent (only its extension is used).</param>
    /// <param name="content">The file content.</param>
    /// <returns>The values by field code, or an <see cref="UploadErrors"/> error.</returns>
    Result<ReturnFileContent> Read(string fileName, ReadOnlyMemory<byte> content);
}

/// <summary>Writes a return's template, with any values already entered, for download.</summary>
public interface IReturnFileWriter
{
    /// <summary>Writes the file.</summary>
    /// <param name="format">The file format.</param>
    /// <param name="sheet">What to write.</param>
    /// <returns>The file content.</returns>
    byte[] Write(ReturnFileFormat format, ReturnFileSheet sheet);
}

/// <summary>What an uploaded file contained.</summary>
/// <param name="Format">The format the signature check identified.</param>
/// <param name="Values">The raw values by field code, in file order (blank cells are <see langword="null"/>).</param>
public sealed record ReturnFileContent(ReturnFileFormat Format, IReadOnlyList<KeyValuePair<string, string?>> Values)
{
    /// <summary>Gets the MIME type of the format.</summary>
    public string ContentType => ReturnFileFormats.ContentTypeOf(Format);
}

/// <summary>A return as a downloadable sheet: a title block and one row per field.</summary>
/// <param name="Title">The first line, for example <c>Monthly Liquidity Return (MLR) template version 1</c>.</param>
/// <param name="Subtitle">The second line, for example the bank and period.</param>
/// <param name="Rows">One row per field, in display order.</param>
public sealed record ReturnFileSheet(string Title, string Subtitle, IReadOnlyList<ReturnFileRow> Rows);

/// <summary>One field in a downloadable sheet.</summary>
/// <param name="FieldCode">The field code (the column uploads are matched on).</param>
/// <param name="Label">The label.</param>
/// <param name="Section">The section.</param>
/// <param name="DataType">The data type.</param>
/// <param name="Unit">The unit.</param>
/// <param name="Precision">Maximum decimal places for numbers.</param>
/// <param name="Value">The value already entered, if any.</param>
public sealed record ReturnFileRow(
    string FieldCode, string Label, string Section, FieldDataType DataType, string Unit, int Precision, string? Value);

/// <summary>Names, extensions and MIME types of the return file formats.</summary>
public static class ReturnFileFormats
{
    /// <summary>The column holding field codes.</summary>
    public const string FieldCodeColumn = "FieldCode";

    /// <summary>The column holding values.</summary>
    public const string ValueColumn = "Value";

    /// <summary>The MIME type of an Excel workbook.</summary>
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>The MIME type of CSV.</summary>
    public const string CsvContentType = "text/csv";

    /// <summary>Returns the file extension of a format, with the dot.</summary>
    /// <param name="format">The format.</param>
    /// <returns><c>.xlsx</c> or <c>.csv</c>.</returns>
    public static string ExtensionOf(ReturnFileFormat format) => format == ReturnFileFormat.Csv ? ".csv" : ".xlsx";

    /// <summary>Returns the MIME type of a format.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The MIME type.</returns>
    public static string ContentTypeOf(ReturnFileFormat format) => format == ReturnFileFormat.Csv ? CsvContentType : XlsxContentType;

    /// <summary>Returns the format a file name's extension claims, if it is allowed.</summary>
    /// <param name="fileName">The file name.</param>
    /// <returns>The format, or <see langword="null"/> for any other extension.</returns>
    public static ReturnFileFormat? FromFileName(string? fileName) =>
        Path.GetExtension(fileName ?? string.Empty).ToUpperInvariant() switch
        {
            ".XLSX" => ReturnFileFormat.Xlsx,
            ".CSV" => ReturnFileFormat.Csv,
            _ => null,
        };
}

/// <summary>Why an uploaded file was refused. Codes are stable; messages are shown to the maker.</summary>
public static class UploadErrors
{
    /// <summary>The file is empty.</summary>
    public static readonly Error Empty = new("Upload.Empty", "The file is empty.");

    /// <summary>The file is over the size limit.</summary>
    public static readonly Error TooLarge = new(
        "Upload.TooLarge", $"The file is larger than {Domain.Submissions.StoredFile.MaxSizeBytes / (1024 * 1024)} MB.");

    /// <summary>The extension is not on the allow-list.</summary>
    public static readonly Error FileType = new("Upload.FileType", "Only .xlsx and .csv files can be uploaded.");

    /// <summary>The content does not match the extension.</summary>
    public static readonly Error ContentMismatch = new(
        "Upload.ContentMismatch", "The file's content does not match its extension. Save it again as .xlsx or .csv.");

    /// <summary>The workbook contains macros.</summary>
    public static readonly Error MacrosNotAllowed = new(
        "Upload.MacrosNotAllowed", "Workbooks with macros cannot be uploaded. Save it as a plain .xlsx workbook.");

    /// <summary>The file could not be read.</summary>
    public static readonly Error Unreadable = new("Upload.Unreadable", "The file could not be read.");

    /// <summary>The header row is missing.</summary>
    public static readonly Error NoHeader = new(
        "Upload.NoHeader", $"The file needs a header row with {ReturnFileFormats.FieldCodeColumn} and {ReturnFileFormats.ValueColumn} columns. Download the template to see the layout.");

    /// <summary>The file names a field more than once.</summary>
    public static readonly Error DuplicateField = new("Upload.DuplicateField", "The file lists a field more than once.");

    /// <summary>The file names fields that are not in the return.</summary>
    public static readonly Error UnknownFields = new("Upload.UnknownFields", "The file has field codes that are not in this return.");

    /// <summary>The file has no rows.</summary>
    public static readonly Error NoValues = new("Upload.NoValues", "The file has no field rows.");
}
