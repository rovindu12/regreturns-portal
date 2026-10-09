using RegReturns.Domain.Obligations;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;
using RegReturns.Infrastructure.Persistence.Seeding;

namespace RegReturns.UnitTests.Infrastructure;

public sealed class DemoDataBuilderTests
{
    private static readonly DateTimeOffset AnchorDate = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private readonly DemoDataSet _data = new DemoDataBuilder(AnchorDate).Build();

    [Fact]
    public void Builds_five_banks_three_return_types_and_a_year_of_obligations()
    {
        _data.Institutions.Count.ShouldBe(5);
        _data.ReturnTypes.Select(r => r.Code).ShouldBe(["MLR", "MDA", "QCAR"], ignoreOrder: true);
        _data.Templates.ShouldAllBe(t => t.Status == TemplateStatus.Published);

        // 5 banks x (12 monthly MLR + 12 monthly MDA + 4 quarterly QCAR).
        _data.Obligations.Count.ShouldBe(5 * ((2 * DemoDataBuilder.MonthsOfHistory) + DemoDataBuilder.QuartersOfHistory));
    }

    [Fact]
    public void Creates_a_demo_user_for_every_role()
    {
        _data.Users.SelectMany(u => u.Roles).Distinct().Count().ShouldBe(Enum.GetValues<RegReturns.Domain.Identity.Role>().Length);
        _data.Users.ShouldAllBe(u => u.IsDemoAccount);
    }

    [Fact]
    public void History_ends_with_the_last_completed_period()
    {
        var lastMonth = _data.Obligations.Where(o => o.Period.Frequency == RegReturns.Domain.Periods.ReturnFrequency.Monthly)
            .Max(o => o.Period)!;
        lastMonth.Label.ShouldBe("2026-09");
    }

    [Fact]
    public void Nothing_is_dated_after_the_anchor_date()
    {
        _data.Submissions.SelectMany(s => s.Events).ShouldAllBe(e => e.OccurredAt <= AnchorDate);
    }

    [Fact]
    public void A_reset_files_new_returns_for_the_institutions_and_users_it_keeps()
    {
        var later = new DemoDataBuilder(AnchorDate.AddMonths(2), new DemoDirectory(_data.Institutions, _data.Users)).Build();

        later.Institutions.ShouldBe(_data.Institutions);
        later.Users.ShouldBe(_data.Users);
        later.Submissions.Select(s => s.InstitutionId).Distinct().ShouldBeSubsetOf(_data.Institutions.Select(i => i.Id));
        later.Submissions.Select(s => s.Id).Intersect(_data.Submissions.Select(s => s.Id)).ShouldBeEmpty();
        later.Obligations.Where(o => o.Period.Frequency == RegReturns.Domain.Periods.ReturnFrequency.Monthly)
            .Max(o => o.Period)!.Label.ShouldBe("2026-11");
    }

    [Fact]
    public void A_reset_creates_what_the_directory_is_missing()
    {
        var kept = _data.Users.Where(u => u.UserName != "auditor").ToList();

        var later = new DemoDataBuilder(AnchorDate, new DemoDirectory(_data.Institutions, kept)).Build();

        later.Users.Count.ShouldBe(_data.Users.Count);
        later.Users.Single(u => u.UserName == "auditor").ShouldNotBeSameAs(_data.Users.Single(u => u.UserName == "auditor"));
    }

    // The nightly reset (ADR 0031) rebuilds the data for whatever day it runs: every month's figures must pass the
    // error rules and trip only the warnings the scenario justifies, and nothing may be dated after the reset. Ten
    // years of resets, on the first of the month just after the default 03:00 schedule.
    public static TheoryData<DateTimeOffset> ResetDates()
    {
        var dates = new TheoryData<DateTimeOffset>();
        for (var month = new DateTimeOffset(2026, 10, 1, 3, 0, 0, TimeSpan.Zero); month.Year < 2037; month = month.AddMonths(1))
        {
            dates.Add(month);
        }

        return dates;
    }

    [Theory]
    [MemberData(nameof(ResetDates))]
    public void Builds_for_any_reset_date(DateTimeOffset anchor)
    {
        var data = new DemoDataBuilder(anchor).Build();

        data.Submissions.ShouldNotBeEmpty();
        data.Submissions.SelectMany(s => s.Events).ShouldAllBe(e => e.OccurredAt <= anchor);
    }

    [Fact]
    public void Is_deterministic_for_the_same_anchor_date()
    {
        var again = new DemoDataBuilder(AnchorDate).Build();

        static IEnumerable<decimal?> Figures(DemoDataSet d) =>
            d.Submissions.SelectMany(s => s.Values.OrderBy(v => v.FieldCode).Select(v => v.NumericValue));

        Figures(again).ShouldBe(Figures(_data));
    }

    [Fact]
    public void Generated_totals_and_ratios_are_internally_consistent()
    {
        foreach (var v in FiguresFor(MlrTemplate.Code))
        {
            v[MlrTemplate.TotalHqla].ShouldBe(v[MlrTemplate.L1Hqla] + v[MlrTemplate.L2aHqla] + v[MlrTemplate.L2bHqla]);
            v[MlrTemplate.Lcr].ShouldBe(Math.Round(v[MlrTemplate.TotalHqla] / v[MlrTemplate.NetOutflows] * 100m, 2));
        }

        foreach (var v in FiguresFor(MdaTemplate.Code))
        {
            v[MdaTemplate.TotalDeposits].ShouldBe(v[MdaTemplate.DepDemand] + v[MdaTemplate.DepSavings] + v[MdaTemplate.DepTime]);
            v[MdaTemplate.NplRatio].ShouldBe(Math.Round(v[MdaTemplate.NplAmount] / v[MdaTemplate.TotalLoans] * 100m, 2));
        }

        foreach (var v in FiguresFor(QcarTemplate.Code))
        {
            v[QcarTemplate.TotalCapital].ShouldBe(v[QcarTemplate.Cet1] + v[QcarTemplate.At1] + v[QcarTemplate.Tier2]);
            v[QcarTemplate.Car].ShouldBe(Math.Round(v[QcarTemplate.TotalCapital] / v[QcarTemplate.TotalRwa] * 100m, 2));
        }
    }

    [Fact]
    public void Current_period_has_returns_in_every_workflow_queue()
    {
        var statuses = _data.Submissions.Select(s => s.Status).ToHashSet();

        statuses.ShouldContain(SubmissionStatus.Draft);
        statuses.ShouldContain(SubmissionStatus.Submitted);
        statuses.ShouldContain(SubmissionStatus.UnderReview);
        statuses.ShouldContain(SubmissionStatus.Approved);
    }

    [Fact]
    public void Planted_anomalies_are_present()
    {
        // Northgate filed two quarterly capital returns late.
        _data.Submissions.Count(s => s.IsLate).ShouldBe(2);

        // Meridian missed one month, which is now overdue.
        _data.Obligations.Count(o => o.IsOverdue(DateOnly.FromDateTime(AnchorDate.UtcDateTime))).ShouldBe(1);

        // Crestmont's liquidity return was sent back once and approved on revision 2.
        _data.Submissions.Where(s => s.Revision == 2).ShouldHaveSingleItem().Status.ShouldBe(SubmissionStatus.Approved);

        // Lotus Union's LCR breach and Harbourline's NPL jump carry justified warnings.
        var warnings = _data.Submissions.SelectMany(s => s.Findings).ToList();
        warnings.ShouldContain(f => f.RuleCode == MlrTemplate.RuleLcrMinimum);
        warnings.ShouldContain(f => f.RuleCode == MdaTemplate.RuleNplMaximum);
        warnings.ShouldAllBe(f => f.Severity == Severity.Warning && f.Justification != null);
    }

    [Fact]
    public void Obligations_with_approved_returns_are_fulfilled()
    {
        var approvedObligations = _data.Submissions.Where(s => s.Status == SubmissionStatus.Approved).Select(s => s.ObligationId).ToHashSet();

        _data.Obligations.Where(o => approvedObligations.Contains(o.Id)).ShouldAllBe(o => o.Status == ObligationStatus.Fulfilled);
    }

    private IEnumerable<Dictionary<string, decimal>> FiguresFor(string returnCode)
    {
        var returnTypeId = _data.ReturnTypes.Single(r => r.Code == returnCode).Id;
        return _data.Submissions
            .Where(s => s.ReturnTypeId == returnTypeId)
            .Select(s => s.Values.ToDictionary(x => x.FieldCode, x => x.NumericValue!.Value, StringComparer.Ordinal));
    }
}
