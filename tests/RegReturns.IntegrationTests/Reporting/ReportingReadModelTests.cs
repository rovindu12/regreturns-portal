using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using RegReturns.Application.Reporting;
using RegReturns.Domain.Obligations;
using RegReturns.Domain.Periods;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;
using RegReturns.Infrastructure.Persistence;
using RegReturns.Infrastructure.Persistence.Seeding;
using RegReturns.Infrastructure.Reporting;
using RegReturns.IntegrationTests.Infrastructure;
using RegReturns.IntegrationTests.Web;

namespace RegReturns.IntegrationTests.Reporting;

/// <summary>
/// The reporting views and their Dapper read model against the seeded demo data (ADR 0028), seeded on
/// <see cref="SqlServerFixture.SeedDate"/>: twelve months up to 2026-09 and four quarters up to 2026-Q3.
/// </summary>
/// <param name="database">A seeded database of this class's own.</param>
public sealed class ReportingReadModelTests(PortalDatabaseFixture database) : IClassFixture<PortalDatabaseFixture>
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(SqlServerFixture.SeedDate.UtcDateTime);

    private static readonly PeriodWindow Months = new(ReportingPeriod.Monthly(2025, 10), ReportingPeriod.Monthly(2026, 9));

    private static readonly PeriodWindow Quarters = new(ReportingPeriod.Quarterly(2025, 4), ReportingPeriod.Quarterly(2026, 3));

    [Fact]
    public async Task Every_obligation_in_the_window_has_one_row()
    {
        var rows = await ReadModel().GetObligationsAsync(
            new ObligationQuery(null, MlrTemplate.Code, Months), TestContext.Current.CancellationToken);

        rows.Count.ShouldBe(DemoBank.All.Count * 12);
        rows.Select(r => (r.InstitutionCode, r.Period)).Distinct().Count().ShouldBe(rows.Count);
        rows.ShouldAllBe(r => r.ReturnTypeCode == MlrTemplate.Code);
    }

    [Fact]
    public async Task Meridians_missing_deposits_return_is_the_only_overdue_obligation()
    {
        var rows = await ReadModel().GetObligationsAsync(
            new ObligationQuery(null, null, OpenDueBefore: Today), TestContext.Current.CancellationToken);

        var overdue = rows.ShouldHaveSingleItem();
        overdue.InstitutionCode.ShouldBe(DemoBank.Meridian);
        overdue.ReturnTypeCode.ShouldBe(MdaTemplate.Code);
        overdue.Period.ShouldBe(ReportingPeriod.Monthly(2026, 4));
        overdue.Status.ShouldBe(ObligationStatus.Open);
        overdue.FirstSubmittedAt.ShouldBeNull();
        Compliance.StateOf(overdue.Status, overdue.IsLate, overdue.DueDate, Today).ShouldBe(ComplianceState.Overdue);
    }

    [Fact]
    public async Task Northgates_capital_return_was_late_twice()
    {
        var northgate = await InstitutionIdAsync(DemoBank.Northgate);

        var rows = await ReadModel().GetObligationsAsync(
            new ObligationQuery(northgate, QcarTemplate.Code, Quarters), TestContext.Current.CancellationToken);

        rows.Count.ShouldBe(4);
        rows.Where(r => r.IsLate).Select(r => r.Period.Label).ShouldBe(["2025-Q4", "2026-Q2"]);
    }

    [Fact]
    public async Task A_draft_shows_its_status_without_a_first_submission()
    {
        var harbourline = await InstitutionIdAsync(DemoBank.Harbourline);

        var rows = await ReadModel().GetObligationsAsync(
            new ObligationQuery(harbourline, MdaTemplate.Code, new PeriodWindow(Months.To, Months.To)), TestContext.Current.CancellationToken);

        var current = rows.ShouldHaveSingleItem();
        current.Status.ShouldBe(ObligationStatus.Open);
        current.SubmissionStatus.ShouldBe(SubmissionStatus.Draft);
        current.SubmissionFirstSubmittedAt.ShouldBeNull();
    }

    [Fact]
    public async Task A_bank_reads_only_its_own_obligations()
    {
        var lotus = await InstitutionIdAsync(DemoBank.LotusUnion);

        var rows = await ReadModel().GetObligationsAsync(new ObligationQuery(lotus, null, Months), TestContext.Current.CancellationToken);

        rows.ShouldNotBeEmpty();
        rows.ShouldAllBe(r => r.InstitutionCode == DemoBank.LotusUnion);
    }

    [Fact]
    public async Task Findings_count_submitted_revisions_including_ones_sent_back()
    {
        var counts = await ReadModel().GetFindingCountsAsync(
            new FindingQuery(null, MlrTemplate.Code, Months), TestContext.Current.CancellationToken);

        var shock = counts.Where(c => c.Period == ReportingPeriod.Monthly(2026, 6)).Select(c => c.RuleCode).ToList();
        shock.ShouldContain(MlrTemplate.RuleLcrMinimum);
        shock.ShouldContain(MlrTemplate.RuleLcrVariance);
        counts.ShouldContain(c => c.Period == ReportingPeriod.Monthly(2026, 3) && c.RuleCode == MlrTemplate.RuleL2bCap && c.Severity == Severity.Warning);
        counts.ShouldAllBe(c => c.Findings >= c.Returns && c.Returns > 0);
    }

    [Fact]
    public async Task Findings_of_a_draft_never_count()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var context = SqlServerFixture.CreateContext(database.ConnectionString);
        var drafts = await context.Submissions.AsNoTracking()
            .Where(s => s.Status == SubmissionStatus.Draft && s.Revision == 1)
            .Select(s => s.Id)
            .ToListAsync(ct);
        drafts.ShouldNotBeEmpty();

        var viewed = await context.Database
            .SqlQueryRaw<Guid>("SELECT [SubmissionId] AS [Value] FROM [reporting].[SubmittedFindings]")
            .ToListAsync(ct);

        viewed.Intersect(drafts).ShouldBeEmpty();
    }

    [Fact]
    public async Task Approved_values_trace_Harbourlines_jump_in_non_performing_loans()
    {
        var harbourline = await InstitutionIdAsync(DemoBank.Harbourline);

        var values = await ReadModel().GetApprovedValuesAsync(
            new ApprovedValueQuery(harbourline, MdaTemplate.Code, [MdaTemplate.NplRatio], Months), TestContext.Current.CancellationToken);

        // Eleven approved months: the current one is still a draft.
        values.Count.ShouldBe(11);
        values.ShouldAllBe(v => v.FieldCode == MdaTemplate.NplRatio && v.InstitutionCode == DemoBank.Harbourline);
        var byPeriod = values.ToDictionary(v => v.Period.Label, v => v.Value);
        byPeriod["2026-07"].ShouldBeGreaterThan(byPeriod["2026-06"]);
    }

    [Fact]
    public async Task No_field_asked_for_reads_nothing()
    {
        var values = await ReadModel().GetApprovedValuesAsync(
            new ApprovedValueQuery(null, MdaTemplate.Code, [], Months), TestContext.Current.CancellationToken);

        values.ShouldBeEmpty();
    }

    private ReportingReadModel ReadModel() =>
        new(Options.Create(new DatabaseOptions { ConnectionString = database.ConnectionString }));

    private async Task<Guid> InstitutionIdAsync(string code)
    {
        await using var context = SqlServerFixture.CreateContext(database.ConnectionString);
        return await context.Institutions.Where(i => i.Code == code).Select(i => i.Id).SingleAsync(TestContext.Current.CancellationToken);
    }
}
