using RegReturns.Application.Reporting;
using RegReturns.Domain.Obligations;
using RegReturns.Domain.Periods;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;

namespace RegReturns.UnitTests.Application;

public sealed class ReportShapingTests
{
    private static readonly DateOnly Today = new(2026, 10, 4);

    private static readonly ReportingPeriod July = ReportingPeriod.Monthly(2026, 7);
    private static readonly ReportingPeriod August = ReportingPeriod.Monthly(2026, 8);
    private static readonly ReportingPeriod September = ReportingPeriod.Monthly(2026, 9);
    private static readonly IReadOnlyList<ReportingPeriod> Periods = [July, August, September];

    private static readonly ReportInstitution Alpha = new("ALB", "Alpha Bank");
    private static readonly ReportInstitution Beta = new("BTB", "Beta Bank");

    [Fact]
    public void Banks_come_in_code_order_with_one_cell_per_period()
    {
        var report = Compliance([Beta, Alpha], [Obligation(Alpha, July, ObligationStatus.Fulfilled)]);

        report.Rows.Select(r => r.InstitutionCode).ShouldBe(["ALB", "BTB"]);
        report.Rows.ShouldAllBe(r => r.Cells.Count == Periods.Count);
        report.Rows[0].Cells.Select(c => c.Period).ShouldBe(Periods);
    }

    [Fact]
    public void A_period_without_an_obligation_is_an_empty_cell()
    {
        var report = Compliance([Alpha], [Obligation(Alpha, July, ObligationStatus.Fulfilled)]);

        var empty = report.Rows[0].Cells[1];
        empty.State.ShouldBe(ComplianceState.NoObligation);
        empty.DueDate.ShouldBeNull();
    }

    [Fact]
    public void Cells_are_judged_on_the_day()
    {
        var report = Compliance([Alpha], [
            Obligation(Alpha, July, ObligationStatus.Fulfilled),
            Obligation(Alpha, August, ObligationStatus.InProgress, isLate: true),
            Obligation(Alpha, September, ObligationStatus.Open),
        ]);

        report.Rows[0].Cells.Select(c => c.State).ShouldBe([ComplianceState.OnTime, ComplianceState.Late, ComplianceState.NotDue]);
    }

    [Fact]
    public void Regulator_staff_do_not_see_the_status_of_a_draft()
    {
        var draft = Obligation(Alpha, September, ObligationStatus.Open, submission: SubmissionStatus.Draft);

        var report = Compliance([Alpha], [draft], regulatorView: true);

        report.Rows[0].Cells[2].SubmissionStatus.ShouldBeNull();
    }

    [Fact]
    public void A_bank_sees_the_status_of_its_own_draft()
    {
        var draft = Obligation(Alpha, September, ObligationStatus.Open, submission: SubmissionStatus.Draft);

        var report = Compliance([Alpha], [draft], regulatorView: false);

        report.Rows[0].Cells[2].SubmissionStatus.ShouldBe(SubmissionStatus.Draft);
    }

    [Fact]
    public void Regulator_staff_see_the_status_of_a_submitted_return()
    {
        var submitted = Obligation(Alpha, September, ObligationStatus.InProgress, submission: SubmissionStatus.UnderReview);

        var report = Compliance([Alpha], [submitted], regulatorView: true);

        report.Rows[0].Cells[2].SubmissionStatus.ShouldBe(SubmissionStatus.UnderReview);
    }

    [Fact]
    public void Totals_count_cells_by_state_and_the_on_time_rate_counts_due_obligations_only()
    {
        var report = Compliance([Alpha, Beta], [
            Obligation(Alpha, July, ObligationStatus.Fulfilled),
            Obligation(Alpha, August, ObligationStatus.Fulfilled, isLate: true),
            Obligation(Alpha, September, ObligationStatus.Open),
            Obligation(Beta, July, ObligationStatus.Open, due: new DateOnly(2026, 8, 15)),
        ]);

        report.Totals.ShouldBe(new ComplianceTotals(OnTime: 1, Late: 1, Overdue: 1, NotDue: 1));
        report.Totals.Due.ShouldBe(3);
        report.Totals.OnTimeRate.ShouldBe(1m / 3);
    }

    [Fact]
    public void Nothing_due_has_no_on_time_rate()
    {
        new ComplianceTotals(0, 0, 0, 4).OnTimeRate.ShouldBeNull();
    }

    [Fact]
    public void Overdue_items_come_most_overdue_first_with_their_reason()
    {
        var overdue = new[]
        {
            Obligation(Beta, August, ObligationStatus.Open, due: new DateOnly(2026, 9, 15)),
            Obligation(Alpha, July, ObligationStatus.Open, due: new DateOnly(2026, 8, 15), firstSubmittedAt: Today.AddDays(-60)),
        };

        var report = ReportShaping.Compliance([Alpha, Beta], Periods, [], overdue, Today, regulatorView: true);

        report.Overdue.Select(o => (o.InstitutionCode, o.DaysOverdue, o.WasRejected)).ShouldBe([("ALB", 50, true), ("BTB", 19, false)]);
    }

    [Fact]
    public void Findings_are_ordered_by_count_then_errors_first_then_code()
    {
        var trend = ReportShaping.Findings(Periods, [
            new FindingCountRow(July, "B_WARN", Severity.Warning, 2, 2),
            new FindingCountRow(July, "A_WARN", Severity.Warning, 2, 1),
            new FindingCountRow(August, "C_ERR", Severity.Error, 2, 2),
            new FindingCountRow(September, "D_WARN", Severity.Warning, 1, 1),
        ]);

        trend.Rules.Select(r => r.RuleCode).ShouldBe(["C_ERR", "A_WARN", "B_WARN", "D_WARN"]);
    }

    [Fact]
    public void Findings_line_up_with_the_periods_and_add_up()
    {
        var trend = ReportShaping.Findings(Periods, [
            new FindingCountRow(July, "RULE", Severity.Warning, 2, 2),
            new FindingCountRow(September, "RULE", Severity.Warning, 3, 1),
            new FindingCountRow(August, "OTHER", Severity.Error, 1, 1),
        ]);

        var rule = trend.Rules.Single(r => r.RuleCode == "RULE");
        rule.Findings.ShouldBe([2, 0, 3]);
        rule.Total.ShouldBe(5);
        rule.Returns.ShouldBe(3);
        trend.PerPeriod.ShouldBe([2, 1, 3]);
        trend.Total.ShouldBe(6);
    }

    [Fact]
    public void Findings_outside_the_window_are_ignored()
    {
        var trend = ReportShaping.Findings(Periods, [new FindingCountRow(ReportingPeriod.Monthly(2026, 1), "RULE", Severity.Warning, 4, 4)]);

        trend.Rules.ShouldBeEmpty();
        trend.Total.ShouldBe(0);
    }

    [Fact]
    public void Key_ratio_values_line_up_with_the_periods_per_bank()
    {
        var field = new KeyRatioField("LCR", "Liquidity coverage ratio", "%", 2);

        var trend = ReportShaping.KeyRatio(field, Periods, [
            Value(Beta, July, "LCR", 150m),
            Value(Alpha, September, "LCR", 120m),
            Value(Alpha, July, "LCR", 140m),
            Value(Alpha, August, "OTHER", 1m),
        ]);

        trend.Label.ShouldBe("Liquidity coverage ratio");
        trend.Series.Select(s => s.InstitutionCode).ShouldBe(["ALB", "BTB"]);
        trend.Series[0].Values.ShouldBe([140m, null, 120m]);
        trend.Series[1].Values.ShouldBe([150m, null, null]);
    }

    [Fact]
    public void A_series_knows_its_latest_lowest_and_highest_values()
    {
        var series = new KeyRatioSeries("ALB", "Alpha Bank", [140m, 110m, 125m, null]);

        series.Latest.ShouldBe(125m);
        series.Low.ShouldBe(110m);
        series.High.ShouldBe(140m);
    }

    private static ComplianceReport Compliance(
        IReadOnlyList<ReportInstitution> institutions, IReadOnlyList<ObligationRow> obligations, bool regulatorView = true) =>
        ReportShaping.Compliance(institutions, Periods, obligations, [], Today, regulatorView);

    private static ObligationRow Obligation(
        ReportInstitution bank,
        ReportingPeriod period,
        ObligationStatus status,
        bool isLate = false,
        DateOnly? due = null,
        SubmissionStatus? submission = null,
        DateOnly? firstSubmittedAt = null)
    {
        var firstDay = firstSubmittedAt ?? (status == ObligationStatus.Open ? (DateOnly?)null : period.End.AddDays(5));
        DateTimeOffset? first = firstDay is { } day ? new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null;
        var submittedReturn = submission is SubmissionStatus.Draft ? null : first;
        return new ObligationRow(
            Guid.NewGuid(), Guid.NewGuid(), bank.Code, bank.Name, "MLR", period, due ?? period.End.AddDays(15), status, first, isLate, submission, submittedReturn);
    }

    private static ApprovedValueRow Value(ReportInstitution bank, ReportingPeriod period, string field, decimal value) =>
        new(Guid.NewGuid(), bank.Code, bank.Name, period, field, value);
}
