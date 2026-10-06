using RegReturns.Application.Reporting;
using RegReturns.Domain.Periods;

namespace RegReturns.UnitTests.Application;

public sealed class ReportExportTests
{
    private static readonly ReportReturnType Qcar = new(Guid.NewGuid(), "QCAR", "Quarterly Capital Adequacy Return", ReturnFrequency.Quarterly);

    [Fact]
    public void A_report_of_every_bank_is_named_after_the_return_and_day()
    {
        ExportComplianceReportHandler.FileNameOf(Header(null), ReportFormat.Pdf).ShouldBe("compliance-QCAR-2026-10-04.pdf");
    }

    [Fact]
    public void A_banks_own_report_carries_its_code()
    {
        ExportComplianceReportHandler.FileNameOf(Header(new ReportInstitution("HLB", "Harbourline Bank PLC")), ReportFormat.Xlsx)
            .ShouldBe("compliance-QCAR-HLB-2026-10-04.xlsx");
    }

    [Theory]
    [InlineData(ReportFormat.Xlsx, "xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData(ReportFormat.Pdf, "pdf", "application/pdf")]
    public void Each_format_has_its_extension_and_media_type(ReportFormat format, string name, string contentType)
    {
        ExportComplianceReportHandler.FormatName(format).ShouldBe(name);
        ExportComplianceReportHandler.ContentTypeOf(format).ShouldBe(contentType);
    }

    [Fact]
    public void The_header_names_its_window()
    {
        var header = Header(null);

        header.PeriodRange.ShouldBe("2025-Q4 to 2026-Q3");
        header.AllInstitutions.ShouldBeTrue();
    }

    private static ReportHeader Header(ReportInstitution? institution) => new(
        Qcar,
        [Qcar],
        [ReportingPeriod.Quarterly(2025, 4), ReportingPeriod.Quarterly(2026, 1), ReportingPeriod.Quarterly(2026, 2), ReportingPeriod.Quarterly(2026, 3)],
        new DateOnly(2026, 10, 4),
        institution);
}
