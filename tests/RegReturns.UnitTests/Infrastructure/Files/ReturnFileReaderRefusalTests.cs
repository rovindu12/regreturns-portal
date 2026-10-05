using System.Globalization;
using System.IO.Compression;
using System.Text;

using Microsoft.Extensions.Logging;

using RegReturns.Application.Returns;
using RegReturns.Domain.Submissions;
using RegReturns.Infrastructure.Files;
using RegReturns.UnitTests.Auditing;

using static RegReturns.UnitTests.Infrastructure.Files.ReturnFiles;

namespace RegReturns.UnitTests.Infrastructure.Files;

public sealed class ReturnFileReaderRefusalTests
{
    private const string Header = "FieldCode,Value\r\n";

    [Theory]
    [InlineData("return.xlsm")]
    [InlineData("return.xls")]
    [InlineData("return.pdf")]
    [InlineData("return.csv.exe")]
    [InlineData("return")]
    [InlineData("")]
    public void A_file_with_another_extension_is_refused(string fileName)
    {
        var result = Reader().Read(fileName, Utf8(Header + "F1,1\r\n"));

        result.Error.ShouldBe(UploadErrors.FileType);
    }

    [Theory]
    [InlineData("return.xlsx")]
    [InlineData("return.csv")]
    public void An_empty_file_is_refused(string fileName)
    {
        Reader().Read(fileName, ReadOnlyMemory<byte>.Empty).Error.ShouldBe(UploadErrors.Empty);
    }

    [Fact]
    public void A_file_over_the_size_limit_is_refused()
    {
        var content = new byte[StoredFile.MaxSizeBytes + 1];
        content.AsSpan().Fill((byte)'a');

        Reader().Read("return.csv", content).Error.ShouldBe(UploadErrors.TooLarge);
    }

    [Theory]
    [InlineData("pdf", "return.xlsx")]
    [InlineData("pdf", "return.csv")]
    [InlineData("exe", "return.xlsx")]
    [InlineData("exe", "return.csv")]
    public void Pdf_or_exe_bytes_with_an_allowed_extension_are_refused(string kind, string fileName)
    {
        var content = kind == "pdf" ? PdfBytes() : ExeBytes();

        Reader().Read(fileName, content).Error.ShouldBe(UploadErrors.ContentMismatch);
    }

    [Fact]
    public void A_pdf_without_binary_bytes_renamed_csv_is_refused()
    {
        Reader().Read("return.csv", Utf8("%PDF-1.4\nFieldCode,Value\nF1,1\n")).Error.ShouldBe(UploadErrors.ContentMismatch);
    }

    [Fact]
    public void A_zip_renamed_csv_is_refused()
    {
        var zip = Zip(("data.csv", Utf8(Header + "F1,1\r\n")));

        Reader().Read("return.csv", zip).Error.ShouldBe(UploadErrors.ContentMismatch);
    }

    [Fact]
    public void A_workbook_renamed_csv_is_refused()
    {
        var workbook = Table(("F1", 1));

        Reader().Read("return.csv", workbook).Error.ShouldBe(UploadErrors.ContentMismatch);
    }

    [Fact]
    public void A_csv_renamed_xlsx_is_refused()
    {
        ReadXlsx(Utf8(Header + "F1,1\r\n")).Error.ShouldBe(UploadErrors.ContentMismatch);
    }

    [Fact]
    public void A_zip_without_content_types_renamed_xlsx_is_refused()
    {
        var zip = Zip(("xl/workbook.xml", Utf8("<workbook/>")));

        ReadXlsx(zip).Error.ShouldBe(UploadErrors.ContentMismatch);
    }

    [Fact]
    public void A_macro_enabled_workbook_is_refused()
    {
        var macroTypes = """<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="bin" ContentType="application/vnd.ms-office.vbaProject"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.ms-excel.sheet.macroEnabled.main+xml"/></Types>""";
        var workbook = WithEntries(
            Table(("F1", 1)),
            ("[Content_Types].xml", Utf8(macroTypes)),
            ("xl/vbaProject.bin", [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]));

        ReadXlsx(workbook).Error.ShouldBe(UploadErrors.MacrosNotAllowed);
    }

    [Fact]
    public void A_vba_project_is_refused_even_when_the_content_types_look_plain()
    {
        var workbook = WithEntries(Table(("F1", 1)), ("xl/vbaProject.bin", [0x01, 0x02, 0x03]));

        ReadXlsx(workbook).Error.ShouldBe(UploadErrors.MacrosNotAllowed);
    }

    [Fact]
    public void A_macro_enabled_content_type_is_refused_even_when_spelled_with_character_references()
    {
        // "macro&#69;nabled" is "macroEnabled" once parsed, so a plain text search would miss it.
        var types = """<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.ms-excel.sheet.macro&#69;nabled.main+xml"/></Types>""";
        var workbook = WithEntries(Table(("F1", 1)), ("[Content_Types].xml", Utf8(types)));

        ReadXlsx(workbook).Error.ShouldBe(UploadErrors.MacrosNotAllowed);
    }

    [Fact]
    public void An_entry_that_expands_more_than_a_hundredfold_is_refused()
    {
        var bomb = Zip(("[Content_Types].xml", Utf8(ContentTypesXml)), ("xl/worksheets/sheet1.xml", new byte[2 * 1024 * 1024]));

        ReadXlsx(bomb).Error.ShouldBe(ReadErrors.TooComplex);
    }

    [Fact]
    public void A_package_with_more_than_200_entries_is_refused()
    {
        var entries = Enumerable.Range(0, 200)
            .Select(i => (string.Create(CultureInfo.InvariantCulture, $"xl/media/part{i}.xml"), Utf8("<a/>")))
            .Prepend(("[Content_Types].xml", Utf8(ContentTypesXml)))
            .ToArray();

        ReadXlsx(Zip(entries)).Error.ShouldBe(ReadErrors.TooComplex);
    }

    [Fact]
    public void A_package_that_expands_past_the_total_limit_is_refused()
    {
        var reader = Reader(ReturnFileLimits.Default with { MaxUncompressedBytes = 4_096 });

        reader.Read("return.xlsx", Table(("F1", 1))).Error.ShouldBe(ReadErrors.TooComplex);
    }

    [Fact]
    public void A_compressed_entry_that_understates_its_size_is_refused()
    {
        var data = Utf8(string.Concat(Enumerable.Range(0, 2_000).Select(i => string.Create(CultureInfo.InvariantCulture, $"<c r=\"A{i}\"/>"))));
        var package = Zip(("[Content_Types].xml", Utf8(ContentTypesXml)), ("xl/worksheets/sheet1.xml", data));

        // The runtime's deflate stream stops at the declared size, so the refusal can come from any later check.
        var result = ReadXlsx(DeclareUncompressedSize(package, data.Length, declaredSize: 100));

        result.Error!.Code.ShouldBe(UploadErrors.Unreadable.Code);
    }

    [Fact]
    public void A_stored_entry_that_understates_its_size_is_refused()
    {
        var data = Utf8(string.Concat(Enumerable.Repeat("<row/>", 4_000)));
        var package = Zip(CompressionLevel.NoCompression, ("[Content_Types].xml", Utf8(ContentTypesXml)), ("xl/worksheets/sheet1.xml", data));

        var result = ReadXlsx(DeclareUncompressedSize(package, data.Length, declaredSize: 100));

        result.Error.ShouldBe(ReadErrors.TooComplex);
    }

    [Fact]
    public void A_damaged_zip_is_refused()
    {
        byte[] content = [0x50, 0x4B, 0x03, 0x04, .. Encoding.ASCII.GetBytes("not really a zip archive")];

        ReadXlsx(content).Error.ShouldBe(UploadErrors.Unreadable);
    }

    [Fact]
    public void A_package_that_is_not_a_workbook_is_refused_with_a_generic_message()
    {
        var package = Zip(("[Content_Types].xml", Utf8(ContentTypesXml)), ("xl/workbook.xml", Utf8("<workbook>broken")));

        ReadXlsx(package).Error.ShouldBe(UploadErrors.Unreadable);
    }

    [Fact]
    public void An_unreadable_workbook_logs_the_exception_type_only()
    {
        var logger = new CapturingLogger<ReturnFileReader>();
        var package = Zip(("[Content_Types].xml", Utf8(ContentTypesXml)), ("xl/workbook.xml", Utf8("<workbook>secret 1234.5")));

        Reader(logger: logger).Read("return.xlsx", package);

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Exception.ShouldBeNull();
        entry.Message.ShouldNotContain("1234.5");
    }

    [Fact]
    public void Csv_that_is_not_valid_utf8_is_refused()
    {
        byte[] content = [.. Utf8(Header + "F1,caf"), 0xE9, .. Utf8("\r\n")];

        Reader().Read("return.csv", content).Error.ShouldBe(UploadErrors.ContentMismatch);
    }

    [Fact]
    public void Csv_with_a_nul_byte_is_refused()
    {
        byte[] content = [.. Utf8(Header + "F1,1"), 0x00, .. Utf8("\r\n")];

        Reader().Read("return.csv", content).Error.ShouldBe(UploadErrors.ContentMismatch);
    }

    [Fact]
    public void A_utf16_csv_is_refused()
    {
        Reader().Read("return.csv", Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(Header)).ToArray())
            .Error.ShouldBe(UploadErrors.ContentMismatch);
    }

    [Fact]
    public void A_workbook_without_a_header_row_is_refused()
    {
        var workbook = Workbook(sheet =>
        {
            sheet.Cell(1, 1).Value = "Code";
            sheet.Cell(1, 2).Value = "Amount";
            sheet.Cell(2, 1).Value = "F1";
        });

        ReadXlsx(workbook).Error.ShouldBe(UploadErrors.NoHeader);
    }

    [Fact]
    public void An_empty_workbook_is_refused_for_having_no_header()
    {
        ReadXlsx(Workbook(_ => { })).Error.ShouldBe(UploadErrors.NoHeader);
    }

    [Fact]
    public void A_csv_without_a_header_row_is_refused()
    {
        ReadCsv("Code,Amount\r\nF1,1\r\n").Error.ShouldBe(UploadErrors.NoHeader);
    }

    [Fact]
    public void A_header_below_the_first_20_rows_is_not_found()
    {
        ReadCsv(string.Concat(Enumerable.Repeat("title\r\n", 20)) + Header + "F1,1\r\n").Error.ShouldBe(UploadErrors.NoHeader);
    }

    [Fact]
    public void A_code_listed_twice_in_a_workbook_is_refused_and_named()
    {
        var error = ReadXlsx(Table(("F1", 1), ("TOTAL_HQLA", 2), ("TOTAL_HQLA", 3))).Error!;

        error.Code.ShouldBe(UploadErrors.DuplicateField.Code);
        error.Message.ShouldContain("TOTAL_HQLA");
    }

    [Fact]
    public void A_code_listed_twice_in_a_csv_is_refused_and_named()
    {
        var error = ReadCsv(Header + "TOTAL_HQLA,1\r\n TOTAL_HQLA ,2\r\n").Error!;

        error.Code.ShouldBe(UploadErrors.DuplicateField.Code);
        error.Message.ShouldContain("TOTAL_HQLA");
    }

    [Fact]
    public void A_workbook_with_a_header_and_no_rows_is_refused()
    {
        ReadXlsx(Table()).Error.ShouldBe(UploadErrors.NoValues);
    }

    [Fact]
    public void A_csv_whose_rows_all_lack_a_code_is_refused()
    {
        ReadCsv(Header + ",1\r\n  ,2\r\n\r\n").Error.ShouldBe(UploadErrors.NoValues);
    }

    [Fact]
    public void More_rows_than_the_limit_are_refused()
    {
        var rows = string.Concat(Enumerable.Range(1, 2_001).Select(i => string.Create(CultureInfo.InvariantCulture, $"F{i},1\r\n")));

        var error = ReadCsv(Header + rows).Error!;

        error.Code.ShouldBe(UploadErrors.Unreadable.Code);
        error.Message.ShouldContain("2,000");
    }

    [Fact]
    public void A_csv_field_over_the_length_limit_is_refused()
    {
        var error = ReadCsv(Header + "F1," + new string('9', 4_001) + "\r\n").Error!;

        error.ShouldBe(ReadErrors.CellTooLong(ReturnFileLimits.Default));
    }

    [Fact]
    public void A_quoted_csv_field_over_the_length_limit_is_refused()
    {
        var error = ReadCsv(Header + "F1,\"" + new string('9', 4_001) + "\"\r\n").Error!;

        error.ShouldBe(ReadErrors.CellTooLong(ReturnFileLimits.Default));
    }

    [Fact]
    public void A_workbook_cell_over_the_length_limit_is_refused()
    {
        ReadXlsx(Table(("F1", new string('9', 4_001)))).Error.ShouldBe(ReadErrors.CellTooLong(ReturnFileLimits.Default));
    }

    [Fact]
    public void A_csv_quote_that_is_never_closed_is_refused_naming_its_line()
    {
        var error = ReadCsv(Header + "F1,1\r\nF2,\"open\r\nF3,3\r\n").Error!;

        error.ShouldBe(ReadErrors.UnclosedQuote(3));
    }

    [Fact]
    public void Text_after_a_closing_quote_is_refused()
    {
        ReadCsv(Header + "F1,\"1\"2\r\n").Error.ShouldBe(ReadErrors.NotCsv(2));
    }

    [Fact]
    public void A_csv_line_with_too_many_columns_is_refused()
    {
        ReadCsv(Header + "F1" + new string(',', 200) + "\r\n").Error.ShouldBe(ReadErrors.TooManyColumns(2, ReturnFileLimits.Default));
    }

    [Fact]
    public void A_number_too_large_to_be_a_return_figure_is_refused()
    {
        ReadXlsx(Table(("F1", 1e30))).Error.ShouldBe(ReadErrors.NumberOutOfRange);
    }
}
