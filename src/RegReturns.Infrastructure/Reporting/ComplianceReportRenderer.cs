using System.Globalization;

using ClosedXML.Excel;

using QuestPDF;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

using RegReturns.Application.Reporting;
using RegReturns.Infrastructure.Files;

namespace RegReturns.Infrastructure.Reporting;

/// <summary>
/// Writes the compliance report (ADR 0028). The workbook has three sheets: the bank × period grid with counts per bank,
/// the overdue list, and every obligation in the window; strings go in as text, never formulas. The PDF is A4 landscape
/// with the grid, a legend and the overdue list; every state is spelt out, so the colours are never the only cue.
/// </summary>
public sealed class ComplianceReportRenderer : IComplianceReportRenderer
{
    /// <summary>The name of the grid sheet.</summary>
    public const string ComplianceSheet = "Compliance";

    /// <summary>The name of the overdue sheet.</summary>
    public const string OverdueSheet = "Overdue";

    /// <summary>The name of the obligations sheet.</summary>
    public const string ObligationsSheet = "Obligations";

    /// <summary>The workbook row holding the column headers (below the title, subtitle and a blank row).</summary>
    public const int HeaderRow = 4;

    private const string Regulator = "Bank of Valoria";
    private const string DateFormat = "yyyy-MM-dd";
    private const string DateTimeFormat = "yyyy-MM-dd HH:mm";
    private const string ExcelDateFormat = "yyyy-mm-dd";
    private const string ExcelDateTimeFormat = "yyyy-mm-dd hh:mm";

    private static readonly string[] OverdueColumns = ["Bank", "Return", "Period", "Due date", "Days overdue", "Reason"];

    private static readonly ComplianceState[] Legend =
        [ComplianceState.OnTime, ComplianceState.Late, ComplianceState.Overdue, ComplianceState.NotDue, ComplianceState.NoObligation];

    static ComplianceReportRenderer()
    {
        // The Community licence covers this project (ADR 0028); QuestPDF refuses to render until one is chosen.
        Settings.License = LicenseType.Community;
    }

    /// <inheritdoc />
    public byte[] Render(ComplianceReportDocument document, ReportFormat format)
    {
        ArgumentNullException.ThrowIfNull(document);
        return format switch
        {
            ReportFormat.Xlsx => RenderXlsx(document),
            ReportFormat.Pdf => RenderPdf(document),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown report format."),
        };
    }

    /// <summary>Returns the report's title, such as <c>Filing compliance: Monthly Liquidity Return (MLR)</c>.</summary>
    /// <param name="header">What the report covers.</param>
    /// <returns>The title.</returns>
    internal static string TitleOf(ReportHeader header) =>
        $"Filing compliance: {header.ReturnType.Name} ({header.ReturnType.Code})";

    /// <summary>Returns the line under the title: who, which periods, and when.</summary>
    /// <param name="document">The report.</param>
    /// <returns>The subtitle.</returns>
    internal static string SubtitleOf(ComplianceReportDocument document)
    {
        var header = document.Header;
        var scope = header.Institution is null ? "All banks" : header.Institution.Name;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Regulator} · {scope} · {header.PeriodRange} · as of {header.AsOf.ToString(DateFormat, CultureInfo.InvariantCulture)} · generated {document.GeneratedAt.UtcDateTime.ToString(DateTimeFormat, CultureInfo.InvariantCulture)} UTC");
    }

    private static (string Background, string Text) ColoursOf(ComplianceState state) => state switch
    {
        ComplianceState.OnTime => ("#D1E7DD", "#0A3622"),
        ComplianceState.Late => ("#FFF3CD", "#664D03"),
        ComplianceState.Overdue => ("#F8D7DA", "#58151C"),
        ComplianceState.NotDue => ("#E2E3E5", "#2B2F32"),
        _ => ("#FFFFFF", "#6C757D"),
    };

    private static string Reason(OverdueItem item) => item.WasRejected ? "Rejected, not filed again" : "Never submitted";

    private static byte[] RenderXlsx(ComplianceReportDocument document)
    {
        using var workbook = new XLWorkbook();
        workbook.Properties.Title = TitleOf(document.Header);
        workbook.Properties.Author = "RegReturns";
        WriteGrid(workbook.AddWorksheet(ComplianceSheet), document);
        WriteOverdue(workbook.AddWorksheet(OverdueSheet), document);
        WriteObligations(workbook.AddWorksheet(ObligationsSheet), document);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteTitle(IXLWorksheet sheet, ComplianceReportDocument document, string title)
    {
        ReturnFileWriter.SetText(sheet.Cell(1, 1), title);
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;
        ReturnFileWriter.SetText(sheet.Cell(2, 1), SubtitleOf(document));
    }

    private static void WriteHeaders(IXLWorksheet sheet, IReadOnlyList<(string Name, double Width)> columns)
    {
        for (var column = 1; column <= columns.Count; column++)
        {
            ReturnFileWriter.SetText(sheet.Cell(HeaderRow, column), columns[column - 1].Name);
            sheet.Column(column).Width = columns[column - 1].Width;
        }

        var header = sheet.Range(HeaderRow, 1, HeaderRow, columns.Count);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#E9ECEF");
        sheet.SheetView.FreezeRows(HeaderRow);
    }

    private static void WriteGrid(IXLWorksheet sheet, ComplianceReportDocument document)
    {
        WriteTitle(sheet, document, TitleOf(document.Header));
        var periods = document.Header.Periods;
        var counted = new[] { ComplianceState.OnTime, ComplianceState.Late, ComplianceState.Overdue };
        WriteHeaders(sheet, [
            ("Bank code", 12),
            ("Bank", 32),
            .. periods.Select(p => (p.Label, 10.0)),
            .. counted.Select(s => (ReportLabels.Of(s), 10.0)),
        ]);

        var row = HeaderRow;
        foreach (var bank in document.Compliance.Rows)
        {
            row++;
            ReturnFileWriter.SetText(sheet.Cell(row, 1), bank.InstitutionCode);
            ReturnFileWriter.SetText(sheet.Cell(row, 2), bank.InstitutionName);
            for (var i = 0; i < bank.Cells.Count; i++)
            {
                var cell = sheet.Cell(row, 3 + i);
                var state = bank.Cells[i].State;
                ReturnFileWriter.SetText(cell, ReportLabels.ShortOf(state));
                var (background, text) = ColoursOf(state);
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml(background);
                cell.Style.Font.FontColor = XLColor.FromHtml(text);
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            for (var i = 0; i < counted.Length; i++)
            {
                sheet.Cell(row, 3 + periods.Count + i).Value = bank.Cells.Count(c => c.State == counted[i]);
            }
        }

        var totals = document.Compliance.Totals;
        row += 2;
        ReturnFileWriter.SetText(sheet.Cell(row, 1), "Filed on time");
        sheet.Cell(row, 2).Value = totals.OnTimeRate is { } rate ? (double)rate : Blank.Value;
        sheet.Cell(row, 2).Style.NumberFormat.Format = "0.0%";
        foreach (var (label, value) in new[] { ("On time", totals.OnTime), ("Late", totals.Late), ("Overdue", totals.Overdue), ("Not due yet", totals.NotDue) })
        {
            row++;
            ReturnFileWriter.SetText(sheet.Cell(row, 1), label);
            sheet.Cell(row, 2).Value = value;
        }
    }

    private static void WriteOverdue(IXLWorksheet sheet, ComplianceReportDocument document)
    {
        WriteTitle(sheet, document, "Overdue returns (every return type)");
        WriteHeaders(sheet, [("Bank code", 12), ("Bank", 32), ("Return", 10), ("Period", 10), ("Due date", 12), ("Days overdue", 14), ("Reason", 28)]);
        var row = HeaderRow;
        foreach (var item in document.Compliance.Overdue)
        {
            row++;
            ReturnFileWriter.SetText(sheet.Cell(row, 1), item.InstitutionCode);
            ReturnFileWriter.SetText(sheet.Cell(row, 2), item.InstitutionName);
            ReturnFileWriter.SetText(sheet.Cell(row, 3), item.ReturnTypeCode);
            ReturnFileWriter.SetText(sheet.Cell(row, 4), item.Period.Label);
            sheet.Cell(row, 5).Value = item.DueDate.ToDateTime(TimeOnly.MinValue);
            sheet.Cell(row, 5).Style.NumberFormat.Format = ExcelDateFormat;
            sheet.Cell(row, 6).Value = item.DaysOverdue;
            ReturnFileWriter.SetText(sheet.Cell(row, 7), Reason(item));
        }

        if (row == HeaderRow)
        {
            ReturnFileWriter.SetText(sheet.Cell(row + 1, 1), "No return is overdue.");
        }
    }

    private static void WriteObligations(IXLWorksheet sheet, ComplianceReportDocument document)
    {
        WriteTitle(sheet, document, $"Obligations: {document.Header.ReturnType.Code}, {document.Header.PeriodRange}");
        WriteHeaders(sheet, [
            ("Bank code", 12), ("Bank", 32), ("Period", 10), ("Due date", 12), ("Compliance", 14), ("First submitted (UTC)", 20), ("Workflow status", 24),
        ]);
        var row = HeaderRow;
        foreach (var bank in document.Compliance.Rows)
        {
            foreach (var cell in bank.Cells.Where(c => c.State != ComplianceState.NoObligation))
            {
                row++;
                ReturnFileWriter.SetText(sheet.Cell(row, 1), bank.InstitutionCode);
                ReturnFileWriter.SetText(sheet.Cell(row, 2), bank.InstitutionName);
                ReturnFileWriter.SetText(sheet.Cell(row, 3), cell.Period.Label);
                if (cell.DueDate is { } due)
                {
                    sheet.Cell(row, 4).Value = due.ToDateTime(TimeOnly.MinValue);
                    sheet.Cell(row, 4).Style.NumberFormat.Format = ExcelDateFormat;
                }

                ReturnFileWriter.SetText(sheet.Cell(row, 5), ReportLabels.Of(cell.State));
                if (cell.FirstSubmittedAt is { } submitted)
                {
                    sheet.Cell(row, 6).Value = submitted.UtcDateTime;
                    sheet.Cell(row, 6).Style.NumberFormat.Format = ExcelDateTimeFormat;
                }

                ReturnFileWriter.SetText(sheet.Cell(row, 7), cell.SubmissionStatus is { } status ? ReportLabels.Of(status) : null);
            }
        }

        if (row > HeaderRow)
        {
            sheet.Range(HeaderRow, 1, row, 7).SetAutoFilter();
        }
    }

    private static byte[] RenderPdf(ComplianceReportDocument document) =>
        Document.Create(container => container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.2f, Unit.Centimetre);
                page.DefaultTextStyle(text => text.FontSize(8.5f));
                page.Header().PaddingBottom(8).Column(column =>
                {
                    column.Item().Text(TitleOf(document.Header)).FontSize(15).Bold();
                    column.Item().Text(SubtitleOf(document)).FontColor(Colors.Grey.Darken2);
                });
                page.Content().Column(column =>
                {
                    column.Spacing(10);
                    column.Item().Element(c => Summary(c, document.Compliance.Totals));
                    column.Item().Element(c => LegendRow(c));
                    column.Item().Element(c => GridTable(c, document));
                    column.Item().PaddingTop(6).Text("Overdue returns (every return type)").FontSize(11).Bold();
                    column.Item().Element(c => OverdueTable(c, document.Compliance.Overdue));
                });
                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text("RegReturns · fictional demonstration data").FontColor(Colors.Grey.Darken1);
                    row.RelativeItem().AlignRight().Text(text =>
                    {
                        text.Span("Page ");
                        text.CurrentPageNumber();
                        text.Span(" of ");
                        text.TotalPages();
                    });
                });
            }))
            .WithMetadata(new DocumentMetadata
            {
                Title = TitleOf(document.Header),
                Author = "RegReturns",
                Subject = SubtitleOf(document),
                CreationDate = document.GeneratedAt,
                ModifiedDate = document.GeneratedAt,
            })
            .GeneratePdf();

    private static void Summary(IContainer container, ComplianceTotals totals)
    {
        var rate = totals.OnTimeRate is { } value ? value.ToString("P1", CultureInfo.InvariantCulture) : "n/a";
        container.Text(text =>
        {
            text.Span("Filed on time: ").Bold();
            text.Span(string.Create(CultureInfo.InvariantCulture, $"{rate} of {totals.Due} due · "));
            text.Span(string.Create(CultureInfo.InvariantCulture, $"on time {totals.OnTime} · late {totals.Late} · overdue {totals.Overdue} · not due yet {totals.NotDue}"));
        });
    }

    private static void LegendRow(IContainer container) => container.Row(row =>
    {
        row.Spacing(6);
        foreach (var state in Legend)
        {
            var (background, text) = ColoursOf(state);
            row.AutoItem().Background(background).Border(0.5f).BorderColor(Colors.Grey.Lighten1).PaddingHorizontal(5).PaddingVertical(2)
                .Text(ReportLabels.ShortOf(state) == ReportLabels.Of(state) ? ReportLabels.Of(state) : $"{ReportLabels.ShortOf(state)} = {ReportLabels.Of(state)}")
                .FontColor(text);
        }
    });

    private static void GridTable(IContainer container, ComplianceReportDocument document)
    {
        var periods = document.Header.Periods;
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(150);
                foreach (var _ in periods)
                {
                    columns.RelativeColumn();
                }
            });
            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Bank");
                foreach (var period in periods)
                {
                    header.Cell().Element(HeaderCell).AlignCenter().Text(period.Label);
                }
            });
            foreach (var bank in document.Compliance.Rows)
            {
                table.Cell().Element(BodyCell).Text($"{bank.InstitutionCode} · {bank.InstitutionName}");
                foreach (var state in bank.Cells.Select(c => c.State))
                {
                    var (background, text) = ColoursOf(state);
                    table.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten2).Background(background).PaddingVertical(3).AlignCenter()
                        .Text(ReportLabels.ShortOf(state)).FontColor(text).FontSize(7.5f);
                }
            }
        });
    }

    private static void OverdueTable(IContainer container, IReadOnlyList<OverdueItem> overdue)
    {
        if (overdue.Count == 0)
        {
            container.Text("No return is overdue.");
            return;
        }

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(3);
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn(2);
            });
            table.Header(header =>
            {
                foreach (var name in OverdueColumns)
                {
                    header.Cell().Element(HeaderCell).Text(name);
                }
            });
            foreach (var item in overdue)
            {
                table.Cell().Element(BodyCell).Text($"{item.InstitutionCode} · {item.InstitutionName}");
                table.Cell().Element(BodyCell).Text(item.ReturnTypeCode);
                table.Cell().Element(BodyCell).Text(item.Period.Label);
                table.Cell().Element(BodyCell).Text(item.DueDate.ToString(DateFormat, CultureInfo.InvariantCulture));
                table.Cell().Element(BodyCell).AlignRight().Text(item.DaysOverdue.ToString(CultureInfo.InvariantCulture));
                table.Cell().Element(BodyCell).Text(Reason(item));
            }
        });
    }

    private static IContainer HeaderCell(IContainer container) =>
        container.Background(Colors.Grey.Lighten3).Border(0.5f).BorderColor(Colors.Grey.Lighten1).PaddingVertical(3).PaddingHorizontal(4)
            .DefaultTextStyle(text => text.Bold());

    private static IContainer BodyCell(IContainer container) =>
        container.Border(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).PaddingHorizontal(4);
}
