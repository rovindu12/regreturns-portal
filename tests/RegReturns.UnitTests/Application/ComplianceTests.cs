using RegReturns.Application.Reporting;
using RegReturns.Domain.Obligations;
using RegReturns.Domain.Periods;

namespace RegReturns.UnitTests.Application;

public sealed class ComplianceTests
{
    private static readonly DateOnly Due = new(2026, 10, 15);

    [Theory]
    [InlineData(ObligationStatus.Open, false, "2026-10-15", ComplianceState.NotDue)]
    [InlineData(ObligationStatus.Open, false, "2026-10-16", ComplianceState.Overdue)]
    [InlineData(ObligationStatus.Open, true, "2026-10-16", ComplianceState.Overdue)]
    [InlineData(ObligationStatus.InProgress, false, "2026-10-20", ComplianceState.OnTime)]
    [InlineData(ObligationStatus.InProgress, true, "2026-10-20", ComplianceState.Late)]
    [InlineData(ObligationStatus.Fulfilled, false, "2026-10-01", ComplianceState.OnTime)]
    [InlineData(ObligationStatus.Fulfilled, true, "2026-12-01", ComplianceState.Late)]
    public void State_follows_the_obligation_status_the_first_submission_and_the_due_date(
        ObligationStatus status, bool isLate, string today, ComplianceState expected)
    {
        Compliance.StateOf(status, isLate, Due, DateOnly.Parse(today, System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(expected);
    }

    [Fact]
    public void Days_overdue_count_from_the_day_after_the_due_date()
    {
        Compliance.DaysOverdue(Due, Due).ShouldBe(0);
        Compliance.DaysOverdue(Due, Due.AddDays(1)).ShouldBe(1);
        Compliance.DaysOverdue(Due, Due.AddDays(-5)).ShouldBe(0);
    }

    [Fact]
    public void The_monthly_window_ends_with_the_last_completed_month()
    {
        Compliance.Window(ReturnFrequency.Monthly, new DateOnly(2026, 10, 4), 3).Select(p => p.Label)
            .ShouldBe(["2026-07", "2026-08", "2026-09"]);
    }

    [Fact]
    public void The_quarterly_window_crosses_a_year_end()
    {
        Compliance.Window(ReturnFrequency.Quarterly, new DateOnly(2026, 10, 4), 4).Select(p => p.Label)
            .ShouldBe(["2025-Q4", "2026-Q1", "2026-Q2", "2026-Q3"]);
    }

    [Fact]
    public void In_january_the_window_ends_in_december()
    {
        Compliance.Window(ReturnFrequency.Monthly, new DateOnly(2027, 1, 1), 2).Select(p => p.Label).ShouldBe(["2026-11", "2026-12"]);
    }

    [Fact]
    public void A_window_has_at_least_one_period()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Compliance.Window(ReturnFrequency.Monthly, new DateOnly(2026, 10, 4), 0));
    }

    [Fact]
    public void A_period_window_lists_its_periods_oldest_first()
    {
        var window = new PeriodWindow(ReportingPeriod.Monthly(2026, 11), ReportingPeriod.Monthly(2027, 2));

        window.Periods.Select(p => p.Label).ShouldBe(["2026-11", "2026-12", "2027-01", "2027-02"]);
        window.Frequency.ShouldBe(ReturnFrequency.Monthly);
        PeriodWindow.Of(window.Periods).ShouldBe(window);
    }

    [Fact]
    public void A_period_window_cannot_run_backwards()
    {
        Should.Throw<ArgumentException>(() => new PeriodWindow(ReportingPeriod.Monthly(2026, 9), ReportingPeriod.Monthly(2026, 8)));
    }

    [Fact]
    public void A_period_window_cannot_mix_frequencies()
    {
        Should.Throw<ArgumentException>(() => new PeriodWindow(ReportingPeriod.Monthly(2026, 1), ReportingPeriod.Quarterly(2026, 4)));
    }

    [Fact]
    public void Dashboards_show_months_or_quarters_by_frequency()
    {
        var options = new ReportingOptions { MonthsShown = 6, QuartersShown = 3 };

        options.PeriodsShown(ReturnFrequency.Monthly).ShouldBe(6);
        options.PeriodsShown(ReturnFrequency.Quarterly).ShouldBe(3);
        new ReportingOptions().KeyRatios.ShouldBeEmpty();
    }

    [Fact]
    public void Every_state_has_its_own_label()
    {
        var states = Enum.GetValues<ComplianceState>();

        states.Select(ReportLabels.Of).Distinct().Count().ShouldBe(states.Length);
        states.Select(ReportLabels.ShortOf).Distinct().Count().ShouldBe(states.Length);
    }
}
