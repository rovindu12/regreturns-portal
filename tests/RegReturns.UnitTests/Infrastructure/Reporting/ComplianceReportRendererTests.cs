using System.Text;

using ClosedXML.Excel;

using RegReturns.Application.Reporting;
using RegReturns.Domain.Periods;
using RegReturns.Domain.Submissions;
using RegReturns.Infrastructure.Reporting;

namespace RegReturns.UnitTests.Infrastructure.Reporting;

public sealed class ComplianceReportRendererTests
{
    private static readonly ReportingPeriod August = ReportingPeriod.Monthly(2026, 8);
    private static readonly ReportingPeriod September = ReportingPeriod.Monthly(2026, 9);
    private static readonly DateTimeOffset Generated = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private readonly ComplianceReportRenderer _renderer = new();

    [Fact]
    public void Workbook_has_the_grid_the_overdue_list_and_the_obligations()
    {
        using var workbook = Open(Document());

        workbook.Worksheets.Select(w => w.Name).ShouldBe(
            [ComplianceReportRenderer.ComplianceSheet, ComplianceReportRenderer.OverdueSheet, ComplianceReportRenderer.ObligationsSheet]);
    }

    [Fact]
    public void Grid_sheet_has_a_title_block_and_one_row_per_bank_with_counts()
    {
        using var workbook = Open(Document());
        var grid = workbook.Worksheet(ComplianceReportRenderer.ComplianceSheet);

        grid.Cell(1, 1).GetText().ShouldBe("Filing compliance: Monthly Liquidity Return (MLR)");
        grid.Cell(2, 1).GetText().ShouldBe("Bank of Valoria · All banks · 2026-08 to 2026-09 · as of 2026-10-04 · generated 2026-10-04 12:00 UTC");
        Enumerable.Range(1, 7).Select(c => grid.Cell(ComplianceReportRenderer.HeaderRow, c).GetText())
            .ShouldBe(["Bank code", "Bank", "2026-08", "2026-09", "On time", "Late", "Overdue"]);
        Enumerable.Range(1, 7).Select(c => grid.Cell(ComplianceReportRenderer.HeaderRow + 1, c).GetFormattedString())
            .ShouldBe(["ALB", "=HYPERLINK(\"x\") Bank", "Late", "Overdue", "0", "1", "1"]);
    }

    [Fact]
    public void Text_that_looks_like_a_formula_stays_text()
    {
        using var workbook = Open(Document());
        var name = workbook.Worksheet(ComplianceReportRenderer.ComplianceSheet).Cell(ComplianceReportRenderer.HeaderRow + 1, 2);

        name.HasFormula.ShouldBeFalse();
        name.DataType.ShouldBe(XLDataType.Text);
    }

    [Fact]
    public void Overdue_sheet_lists_each_overdue_return_with_its_reason()
    {
        using var workbook = Open(Document());
        var sheet = workbook.Worksheet(ComplianceReportRenderer.OverdueSheet);
        var row = ComplianceReportRenderer.HeaderRow + 1;

        Enumerable.Range(1, 7).Select(c => sheet.Cell(row, c).GetFormattedString())
            .ShouldBe(["ALB", "=HYPERLINK(\"x\") Bank", "MDA", "2026-09", "2026-09-21", "13", "Rejected, not filed again"]);
        sheet.Cell(row, 5).DataType.ShouldBe(XLDataType.DateTime);
    }

    [Fact]
    public void Overdue_sheet_says_when_nothing_is_overdue()
    {
        using var workbook = Open(Document() with { Compliance = Document().Compliance with { Overdue = [] } });

        workbook.Worksheet(ComplianceReportRenderer.OverdueSheet).Cell(ComplianceReportRenderer.HeaderRow + 1, 1).GetText()
            .ShouldBe("No return is overdue.");
    }

    [Fact]
    public void Obligations_sheet_skips_empty_cells_and_spells_out_states()
    {
        using var workbook = Open(Document());
        var sheet = workbook.Worksheet(ComplianceReportRenderer.ObligationsSheet);
        var first = ComplianceReportRenderer.HeaderRow + 1;

        Enumerable.Range(1, 7).Select(c => sheet.Cell(first, c).GetFormattedString())
            .ShouldBe(["ALB", "=HYPERLINK(\"x\") Bank", "2026-08", "2026-08-15", "Late", "2026-08-20 09:30", "Approved"]);
        sheet.Cell(first + 1, 5).GetText().ShouldBe("Overdue");
        sheet.Cell(first + 2, 1).GetText().ShouldBe("BTB");
        sheet.Cell(first + 3, 1).IsEmpty().ShouldBeTrue();
    }

    [Fact]
    public void Pdf_is_a_pdf_document_with_the_report_title()
    {
        var pdf = _renderer.Render(Document(), ReportFormat.Pdf);

        Encoding.ASCII.GetString(pdf, 0, 5).ShouldBe("%PDF-");
        pdf.Length.ShouldBeGreaterThan(1000);
    }

    [Fact]
    public void Pdf_renders_when_nothing_is_overdue()
    {
        var pdf = _renderer.Render(Document() with { Compliance = Document().Compliance with { Overdue = [] } }, ReportFormat.Pdf);

        Encoding.ASCII.GetString(pdf, 0, 5).ShouldBe("%PDF-");
    }

    [Fact]
    public void An_unknown_format_is_a_programming_error()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => _renderer.Render(Document(), (ReportFormat)99));
    }

    private static ComplianceReportDocument Document()
    {
        var mlr = new ReportReturnType(Guid.NewGuid(), "MLR", "Monthly Liquidity Return", ReturnFrequency.Monthly);
        var header = new ReportHeader(mlr, [mlr], [August, September], new DateOnly(2026, 10, 4), null);
        const string alphaName = "=HYPERLINK(\"x\") Bank";
        var alpha = new ComplianceRow("ALB", alphaName,
        [
            new ComplianceCell(August, ComplianceState.Late, new DateOnly(2026, 8, 15), new DateTimeOffset(2026, 8, 20, 9, 30, 0, TimeSpan.Zero), SubmissionStatus.Approved),
            new ComplianceCell(September, ComplianceState.Overdue, new DateOnly(2026, 9, 15), null, null),
        ]);
        var beta = new ComplianceRow("BTB", "Beta Bank",
        [
            new ComplianceCell(August, ComplianceState.NoObligation, null, null, null),
            new ComplianceCell(September, ComplianceState.NotDue, new DateOnly(2026, 10, 15), null, SubmissionStatus.Draft),
        ]);
        var overdue = new OverdueItem("ALB", alphaName, "MDA", September, new DateOnly(2026, 9, 21), 13, WasRejected: true);
        return new ComplianceReportDocument(
            header, new ComplianceReport([alpha, beta], new ComplianceTotals(0, 1, 1, 1), [overdue]), Generated);
    }

    private XLWorkbook Open(ComplianceReportDocument document) =>
        new(new MemoryStream(_renderer.Render(document, ReportFormat.Xlsx)));
}
