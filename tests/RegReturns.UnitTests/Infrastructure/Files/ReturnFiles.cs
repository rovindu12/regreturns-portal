using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

using ClosedXML.Excel;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using RegReturns.Application.Returns;
using RegReturns.Domain.Common;
using RegReturns.Infrastructure.Files;

namespace RegReturns.UnitTests.Infrastructure.Files;

/// <summary>Builds upload files for the reader and writer tests: workbooks, CSV text and hand-made ZIP packages.</summary>
internal static class ReturnFiles
{
    public const string ContentTypesXml =
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="xml" ContentType="application/xml"/></Types>""";

    public static ReturnFileReader Reader(ReturnFileLimits? limits = null, ILogger<ReturnFileReader>? logger = null) =>
        new(limits ?? ReturnFileLimits.Default, logger ?? NullLogger<ReturnFileReader>.Instance);

    public static Result<ReturnFileContent> ReadXlsx(byte[] content) => Reader().Read("return.xlsx", content);

    public static Result<ReturnFileContent> ReadCsv(string text) => Reader().Read("return.csv", Utf8(text));

    public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>A workbook whose first worksheet is filled by <paramref name="fill"/>.</summary>
    public static byte[] Workbook(Action<IXLWorksheet> fill, Action<XLWorkbook>? beforeSave = null)
    {
        using var workbook = new XLWorkbook();
        fill(workbook.AddWorksheet("Sheet1"));
        beforeSave?.Invoke(workbook);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>A workbook with a FieldCode/Value header on row 1 and the given rows below it.</summary>
    public static byte[] Table(params (string? Code, XLCellValue Value)[] rows) => Workbook(sheet =>
    {
        sheet.Cell(1, 1).Value = ReturnFileFormats.FieldCodeColumn;
        sheet.Cell(1, 2).Value = ReturnFileFormats.ValueColumn;
        for (var i = 0; i < rows.Length; i++)
        {
            if (rows[i].Code is { } code)
            {
                sheet.Cell(i + 2, 1).Value = code;
            }

            sheet.Cell(i + 2, 2).Value = rows[i].Value;
        }
    });

    /// <summary>The single value read from a one-row workbook whose value cell is set by <paramref name="setValue"/>.</summary>
    public static string? ReadSingleXlsxValue(Action<IXLCell> setValue)
    {
        var content = Workbook(sheet =>
        {
            sheet.Cell(1, 1).Value = ReturnFileFormats.FieldCodeColumn;
            sheet.Cell(1, 2).Value = ReturnFileFormats.ValueColumn;
            sheet.Cell(2, 1).Value = "F1";
            setValue(sheet.Cell(2, 2));
        });
        return ReadXlsx(content).Value.Values.Single().Value;
    }

    /// <summary>A ZIP package with the given entries, each compressed at the optimal level.</summary>
    public static byte[] Zip(params (string Name, byte[] Content)[] entries) => Zip(CompressionLevel.Optimal, entries);

    /// <summary>A ZIP package with the given entries; <see cref="CompressionLevel.NoCompression"/> stores them as they are.</summary>
    public static byte[] Zip(CompressionLevel level, params (string Name, byte[] Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var entry = archive.CreateEntry(name, level).Open();
                entry.Write(content);
            }
        }

        return stream.ToArray();
    }

    /// <summary>Adds or replaces entries in an existing package, such as a workbook ClosedXML wrote.</summary>
    public static byte[] WithEntries(byte[] package, params (string Name, byte[] Content)[] entries)
    {
        using var stream = new MemoryStream();
        stream.Write(package);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                archive.GetEntry(name)?.Delete();
                using var entry = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
                entry.Write(content);
            }
        }

        return stream.ToArray();
    }

    /// <summary>The text of one entry of a package.</summary>
    public static string EntryText(byte[] package, string name)
    {
        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry(name)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Rewrites the uncompressed size a package declares for every entry of <paramref name="actualSize"/> bytes, in
    /// both the local file headers and the central directory.
    /// </summary>
    public static byte[] DeclareUncompressedSize(byte[] package, int actualSize, int declaredSize)
    {
        var patched = package.ToArray();
        Patch(patched, [0x50, 0x4B, 0x03, 0x04], sizeOffset: 22, actualSize, declaredSize);
        Patch(patched, [0x50, 0x4B, 0x01, 0x02], sizeOffset: 24, actualSize, declaredSize);
        return patched;
    }

    /// <summary>Bytes that start like a PDF document, with the binary comment line real PDFs have.</summary>
    public static byte[] PdfBytes() =>
        [.. "%PDF-1.7\n%"u8, 0xE2, 0xE3, 0xCF, 0xD3, .. "\n1 0 obj << /Type /Catalog >> endobj\n%%EOF\n"u8];

    /// <summary>Bytes that start like a Windows executable.</summary>
    public static byte[] ExeBytes() => [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00];

    private static void Patch(byte[] package, byte[] signature, int sizeOffset, int actualSize, int declaredSize)
    {
        var at = package.AsSpan().IndexOf(signature);
        while (at >= 0)
        {
            var size = package.AsSpan(at + sizeOffset, 4);
            if (BinaryPrimitives.ReadInt32LittleEndian(size) == actualSize)
            {
                BinaryPrimitives.WriteInt32LittleEndian(size, declaredSize);
            }

            var next = package.AsSpan(at + 4).IndexOf(signature);
            at = next < 0 ? -1 : at + 4 + next;
        }
    }
}
