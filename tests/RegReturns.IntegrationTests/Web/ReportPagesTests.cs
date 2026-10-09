using System.Net;

using ClosedXML.Excel;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using RegReturns.Application.Reporting;
using RegReturns.Domain.Auditing;
using RegReturns.Domain.Identity;
using RegReturns.Infrastructure.Persistence.Seeding;
using RegReturns.Infrastructure.Reporting;
using RegReturns.IntegrationTests.Hosting;
using RegReturns.IntegrationTests.Infrastructure;

namespace RegReturns.IntegrationTests.Web;

/// <summary>
/// The reports area on the seeded data, judged on <see cref="SqlServerFixture.SeedDate"/> (4 October 2026): regulator
/// staff see every bank but no bank's draft, a bank sees only itself, and every export is a real file recorded in the
/// audit trail.
/// </summary>
[Collection(HostedAppsDefinition.Name)]
public sealed class ReportPagesTests : IClassFixture<PortalDatabaseFixture>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly BankPortal _bank;
    private readonly SupervisionPortal _supervision;
    private readonly string _connectionString;

    public ReportPagesTests(PortalDatabaseFixture database)
    {
        _connectionString = database.ConnectionString;
        _factory = PortalHost.Create(
            database.ConnectionString,
            services => services.AddSingleton<TimeProvider>(new FakeTimeProvider(SqlServerFixture.SeedDate)));
        _bank = new BankPortal(_factory, database.ConnectionString);
        _supervision = new SupervisionPortal(_factory, database.ConnectionString);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Supervisor_sees_every_bank_and_the_overdue_deposits_return()
    {
        using var reviewer = await _supervision.ReviewerAsync();

        var page = await PortalForms.GetTextAsync(reviewer, $"/reports?returnType={MdaTemplate.Code}");

        foreach (var bank in DemoBank.All)
        {
            page.ShouldContain(bank.Name);
        }

        page.ShouldContain($"title=\"{DemoBank.Meridian} 2026-04: Overdue, due 21 May 2026\"");
        page.ShouldContain("<span class=\"badge text-bg-danger\">136</span>");
        page.ShouldContain("Never submitted");
    }

    [Fact]
    public async Task Supervisor_does_not_see_a_banks_draft()
    {
        using var reviewer = await _supervision.ReviewerAsync();

        var page = await PortalForms.GetTextAsync(reviewer, $"/reports?returnType={MdaTemplate.Code}");

        page.ShouldContain($"title=\"{DemoBank.Harbourline} 2026-09: Not due yet, due 21 Oct 2026\"");
    }

    [Fact]
    public async Task Bank_sees_only_itself_including_its_draft()
    {
        using var maker = await _bank.MakerAsync();

        var page = await PortalForms.GetTextAsync(maker, $"/reports?returnType={MdaTemplate.Code}");

        page.ShouldContain($"title=\"{DemoBank.Harbourline} 2026-09: Not due yet, due 21 Oct 2026, draft\"");
        foreach (var other in DemoBank.All.Where(b => b.Code != DemoBank.Harbourline))
        {
            page.ShouldNotContain(other.Name);
        }

        page.ShouldContain("No return is overdue.");
    }

    [Fact]
    public async Task Findings_and_key_ratios_come_from_submitted_and_approved_returns()
    {
        using var auditor = await _supervision.AuditorAsync();

        var page = await PortalForms.GetTextAsync(auditor, $"/reports?returnType={MlrTemplate.Code}");

        page.ShouldContain($"<code>{MlrTemplate.RuleLcrMinimum}</code>");
        page.ShouldContain("Liquidity coverage ratio <span class=\"text-body-secondary fw-normal\">(LCR, approved returns)</span>");
        page.ShouldContain("data-chart=\"{\"kind\":\"findings\"");
        page.ShouldContain("<script src=\"/lib/chart.js/chart.umd.min.js\" nonce=");
    }

    [Fact]
    public async Task The_first_return_type_is_shown_when_none_is_asked_for()
    {
        using var reviewer = await _supervision.ReviewerAsync();

        var page = await PortalForms.GetTextAsync(reviewer, "/reports");

        page.ShouldContain("<strong>Monthly Deposits and Advances Return</strong>");
    }

    [Fact]
    public async Task An_unknown_return_type_is_not_found()
    {
        using var reviewer = await _supervision.ReviewerAsync();

        var response = await reviewer.GetAsync(new Uri("/reports?returnType=NOPE", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Excel_export_is_a_workbook_of_every_bank_and_is_audited()
    {
        using var reviewer = await _supervision.ReviewerAsync();

        var response = await reviewer.GetAsync(new Uri($"/reports/compliance/export?returnType={MdaTemplate.Code}&format=xlsx", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe(ExportComplianceReportHandler.ContentTypeOf(ReportFormat.Xlsx));
        response.Content.Headers.ContentDisposition!.FileNameStar.ShouldBe("compliance-MDA-2026-10-04.xlsx");
        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync(Ct)));
        workbook.Worksheets.Select(w => w.Name).ShouldBe(
            [ComplianceReportRenderer.ComplianceSheet, ComplianceReportRenderer.OverdueSheet, ComplianceReportRenderer.ObligationsSheet]);
        var grid = workbook.Worksheet(ComplianceReportRenderer.ComplianceSheet);
        Enumerable.Range(1, DemoBank.All.Count)
            .Select(i => grid.Cell(ComplianceReportRenderer.HeaderRow + i, 1).GetString())
            .ShouldBe(DemoBank.All.Select(b => b.Code).Order(StringComparer.Ordinal));
        workbook.Worksheet(ComplianceReportRenderer.OverdueSheet).Cell(ComplianceReportRenderer.HeaderRow + 1, 1).GetString().ShouldBe(DemoBank.Meridian);

        var entry = (await ExportEntriesAsync($"it-{DemoUsers.Reviewer}")).ShouldHaveSingleItem();
        entry.EntityType.ShouldBe(ExportComplianceReportHandler.AuditEntityType);
        entry.EntityId.ShouldBe(ExportComplianceReportHandler.ComplianceReportId);
        entry.Details.ShouldBe("Compliance report MDA 2025-10 to 2026-09, all banks, xlsx");
    }

    [Fact]
    public async Task Pdf_export_of_a_bank_is_named_after_it()
    {
        using var checker = await _bank.CheckerAsync();

        var response = await checker.GetAsync(new Uri($"/reports/compliance/export?returnType={QcarTemplate.Code}&format=pdf", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        response.Content.Headers.ContentDisposition!.FileNameStar.ShouldBe($"compliance-QCAR-{DemoBank.Harbourline}-2026-10-04.pdf");
        (await response.Content.ReadAsByteArrayAsync(Ct)).Take(5).ShouldBe("%PDF-"u8.ToArray());
        (await ExportEntriesAsync($"it-{DemoUsers.CheckerUserName(DemoBank.Harbourline)}"))
            .ShouldHaveSingleItem().Details.ShouldBe("Compliance report QCAR 2025-Q4 to 2026-Q3, own bank, pdf");
    }

    [Theory]
    [InlineData("")]
    [InlineData("&format=docx")]
    [InlineData("&format=7")]
    public async Task An_export_without_a_known_format_is_a_bad_request(string format)
    {
        using var admin = await _supervision.MultiRoleUserAsync("report-admin", [Role.SystemAdmin], bankCode: null);

        var response = await admin.GetAsync(new Uri($"/reports/compliance/export?returnType={MlrTemplate.Code}{format}", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    public void Dispose() => _factory.Dispose();

    private async Task<List<AuditEntry>> ExportEntriesAsync(string subject)
    {
        await using var context = SqlServerFixture.CreateContext(_connectionString);
        return await context.AuditEntries.AsNoTracking()
            .Where(e => e.Action == AuditAction.ReportExported && e.ActorSubjectId == subject)
            .ToListAsync(Ct);
    }
}
