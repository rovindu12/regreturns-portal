using RegReturns.Application.Migration;

namespace RegReturns.UnitTests.Application;

public sealed class ReconciliationTests
{
    private static readonly ReconciliationKey HlbJanuary = new("MLR", "HLB", "2024-01");
    private static readonly ReconciliationKey HlbFebruary = new("MLR", "HLB", "2024-02");
    private static readonly ReconciliationKey CcbJanuary = new("MLR", "CCB", "2024-01");
    private static readonly ReconciliationKey HlbQ1 = new("QCAR", "HLB", "2024-Q1");

    [Fact]
    public void Equal_values_match_field_by_field()
    {
        var source = Return(HlbJanuary, ("TOTAL_HQLA", 150m), ("LCR", 120.5m));

        var reconciliation = Reconciliation.Compare([source], [Return(HlbJanuary, ("TOTAL_HQLA", 150.00m), ("LCR", 120.50m))]);

        reconciliation.Lines.Select(l => (l.FieldCode, l.Status)).ShouldBe(
            [("TOTAL_HQLA", ReconciliationStatus.Match), ("LCR", ReconciliationStatus.Match)]);
        reconciliation.Mismatches.ShouldBe(0);
    }

    [Fact]
    public void A_different_value_is_a_mismatch_with_target_minus_source()
    {
        var reconciliation = Reconciliation.Compare(
            [Return(HlbJanuary, ("TOTAL_HQLA", 150m))], [Return(HlbJanuary, ("TOTAL_HQLA", 149.75m))]);

        var line = reconciliation.Lines.ShouldHaveSingleItem();
        line.Status.ShouldBe(ReconciliationStatus.Mismatch);
        line.Difference.ShouldBe(-0.25m);
        reconciliation.Mismatches.ShouldBe(1);
    }

    [Fact]
    public void A_return_missing_from_the_target_is_missing_in_every_field()
    {
        var reconciliation = Reconciliation.Compare([Return(HlbJanuary, ("TOTAL_HQLA", 150m), ("LCR", 120m))], []);

        reconciliation.Lines.ShouldAllBe(l => l.Status == ReconciliationStatus.MissingInTarget && l.Target == null);
        reconciliation.Lines.Count.ShouldBe(2);
        reconciliation.Mismatches.ShouldBe(2);
    }

    [Fact]
    public void A_return_only_in_the_target_is_unexpected()
    {
        var reconciliation = Reconciliation.Compare([], [Return(HlbJanuary, ("TOTAL_HQLA", 150m))]);

        var line = reconciliation.Lines.ShouldHaveSingleItem();
        line.Status.ShouldBe(ReconciliationStatus.UnexpectedInTarget);
        line.Source.ShouldBeNull();
        line.Target.ShouldBe(150m);
    }

    [Fact]
    public void A_field_only_in_the_target_follows_the_source_fields_and_is_unexpected()
    {
        var reconciliation = Reconciliation.Compare(
            [Return(HlbJanuary, ("TOTAL_HQLA", 150m))], [Return(HlbJanuary, ("EXTRA", 1m), ("TOTAL_HQLA", 150m))]);

        reconciliation.Lines.Select(l => (l.FieldCode, l.Status)).ShouldBe(
            [("TOTAL_HQLA", ReconciliationStatus.Match), ("EXTRA", ReconciliationStatus.UnexpectedInTarget)]);
    }

    [Fact]
    public void A_value_the_target_lost_is_missing_in_target()
    {
        var line = Reconciliation.Compare([Return(HlbJanuary, ("LCR", 120m))], [Return(HlbJanuary, ("LCR", null))]).Lines.Single();

        line.Status.ShouldBe(ReconciliationStatus.MissingInTarget);
        line.Difference.ShouldBeNull();
    }

    [Fact]
    public void A_value_the_target_gained_is_unexpected_in_target()
    {
        var line = Reconciliation.Compare([Return(HlbJanuary, ("LCR", null))], [Return(HlbJanuary, ("LCR", 120m))]).Lines.Single();

        line.Status.ShouldBe(ReconciliationStatus.UnexpectedInTarget);
        line.Difference.ShouldBeNull();
    }

    [Fact]
    public void A_field_blank_on_both_sides_matches()
    {
        var line = Reconciliation.Compare([Return(HlbJanuary, ("NPL", null))], [Return(HlbJanuary, ("NPL", null))]).Lines.Single();

        line.Status.ShouldBe(ReconciliationStatus.Match);
        line.Difference.ShouldBeNull();
    }

    [Fact]
    public void Lines_are_ordered_by_return_type_bank_and_period_whatever_the_input_order()
    {
        ReconciledReturn[] returns =
        [
            Return(HlbQ1, ("CAR", 15m)),
            Return(HlbFebruary, ("LCR", 2m)),
            Return(HlbJanuary, ("LCR", 1m)),
            Return(CcbJanuary, ("LCR", 3m)),
        ];

        var reconciliation = Reconciliation.Compare(returns, Enumerable.Reverse(returns));

        reconciliation.Lines.Select(l => l.Key).ShouldBe([CcbJanuary, HlbJanuary, HlbFebruary, HlbQ1]);
    }

    [Fact]
    public void Fields_keep_the_source_order_within_a_return()
    {
        var reconciliation = Reconciliation.Compare(
            [Return(HlbJanuary, ("L1", 1m), ("L2A", 2m), ("TOTAL", 3m))], [Return(HlbJanuary, ("TOTAL", 3m), ("L2A", 2m), ("L1", 1m))]);

        reconciliation.Lines.Select(l => l.FieldCode).ShouldBe(["L1", "L2A", "TOTAL"]);
    }

    [Fact]
    public void Returns_are_counted_on_each_side()
    {
        var reconciliation = Reconciliation.Compare(
            [Return(HlbJanuary, ("LCR", 1m)), Return(HlbFebruary, ("LCR", 2m))], [Return(HlbJanuary, ("LCR", 1m))]);

        reconciliation.SourceReturns.ShouldBe(2);
        reconciliation.TargetReturns.ShouldBe(1);
    }

    [Fact]
    public void Returns_by_institution_count_each_side_and_the_differences_per_bank()
    {
        var reconciliation = Reconciliation.Compare(
            [Return(HlbJanuary, ("LCR", 1m), ("NSFR", 5m)), Return(HlbFebruary, ("LCR", 2m), ("NSFR", 5m)), Return(CcbJanuary, ("LCR", 3m))],
            [Return(HlbJanuary, ("LCR", 1m), ("NSFR", 6m)), Return(CcbJanuary, ("LCR", 3m))]);

        reconciliation.ReturnsByInstitution().ShouldBe(
        [
            new ReconciliationCount("MLR", "CCB", 1, 1, 0),
            new ReconciliationCount("MLR", "HLB", 2, 1, 3),
        ]);
    }

    [Theory]
    [InlineData(2, 2, 0, true)]
    [InlineData(2, 1, 0, false)]
    [InlineData(2, 2, 1, false)]
    public void A_count_matches_when_both_sides_hold_the_same_returns_without_differences(
        int source, int target, int mismatches, bool isMatch)
    {
        new ReconciliationCount("MLR", "HLB", source, target, mismatches).IsMatch.ShouldBe(isMatch);
    }

    [Fact]
    public void Totals_by_institution_sum_each_field_per_bank()
    {
        var reconciliation = Reconciliation.Compare(
            [Return(HlbJanuary, ("LCR", 100m)), Return(HlbFebruary, ("LCR", 50.5m)), Return(CcbJanuary, ("LCR", 30m))],
            [Return(HlbJanuary, ("LCR", 100m)), Return(HlbFebruary, ("LCR", 50m)), Return(CcbJanuary, ("LCR", 30m))]);

        reconciliation.ByInstitution().ShouldBe(
        [
            new ReconciliationTotal("MLR", "CCB", "LCR", 1, 1, 30m, 30m),
            new ReconciliationTotal("MLR", "HLB", "LCR", 2, 2, 150.5m, 150m),
        ]);
    }

    [Fact]
    public void Totals_count_only_the_returns_with_a_value_on_each_side()
    {
        var reconciliation = Reconciliation.Compare(
            [Return(HlbJanuary, ("NPL", null)), Return(HlbFebruary, ("NPL", 4m))],
            [Return(HlbJanuary, ("NPL", null)), Return(HlbFebruary, ("NPL", 4m))]);

        var total = reconciliation.ByInstitution().ShouldHaveSingleItem();
        total.SourceReturns.ShouldBe(1);
        total.TargetReturns.ShouldBe(1);
        total.SourceTotal.ShouldBe(4m);
    }

    [Fact]
    public void Totals_by_period_come_in_return_type_and_period_order()
    {
        var reconciliation = Reconciliation.Compare(
            [Return(HlbFebruary, ("LCR", 2m)), Return(CcbJanuary, ("LCR", 3m)), Return(HlbJanuary, ("LCR", 1m)), Return(HlbQ1, ("CAR", 15m))],
            []);

        reconciliation.ByPeriod().Select(t => (t.ReturnTypeCode, t.Group, t.FieldCode, t.SourceReturns, t.SourceTotal, t.TargetReturns)).ShouldBe(
        [
            ("MLR", "2024-01", "LCR", 2, 4m, 0),
            ("MLR", "2024-02", "LCR", 1, 2m, 0),
            ("QCAR", "2024-Q1", "CAR", 1, 15m, 0),
        ]);
    }

    [Fact]
    public void Totals_by_field_cover_every_bank_and_period_in_one_group()
    {
        var reconciliation = Reconciliation.Compare(
            [Return(HlbJanuary, ("LCR", 1m), ("NSFR", 10m)), Return(CcbJanuary, ("LCR", 3m), ("NSFR", 30m)), Return(HlbQ1, ("CAR", 15m))],
            [Return(HlbJanuary, ("LCR", 1m), ("NSFR", 10m)), Return(CcbJanuary, ("LCR", 3m), ("NSFR", 30m)), Return(HlbQ1, ("CAR", 15m))]);

        reconciliation.ByField().ShouldBe(
        [
            new ReconciliationTotal("MLR", ReconciliationTotal.AllGroups, "LCR", 2, 2, 4m, 4m),
            new ReconciliationTotal("MLR", ReconciliationTotal.AllGroups, "NSFR", 2, 2, 40m, 40m),
            new ReconciliationTotal("QCAR", ReconciliationTotal.AllGroups, "CAR", 1, 1, 15m, 15m),
        ]);
    }

    [Fact]
    public void The_group_across_every_bank_and_period_is_called_all()
    {
        ReconciliationTotal.AllGroups.ShouldBe("ALL");
    }

    [Fact]
    public void A_total_difference_is_target_minus_source()
    {
        new ReconciliationTotal("MLR", "HLB", "LCR", 2, 2, 150.5m, 150m).Difference.ShouldBe(-0.5m);
    }

    [Theory]
    [InlineData(2, 2, "10", "10", true)]
    [InlineData(2, 2, "10", "10.01", false)]
    [InlineData(2, 1, "10", "10", false)]
    public void A_total_matches_when_counts_and_sums_agree(int sourceReturns, int targetReturns, string sourceTotal, string targetTotal, bool isMatch)
    {
        var total = new ReconciliationTotal(
            "MLR", "HLB", "LCR", sourceReturns, targetReturns,
            decimal.Parse(sourceTotal, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(targetTotal, System.Globalization.CultureInfo.InvariantCulture));

        total.IsMatch.ShouldBe(isMatch);
    }

    [Fact]
    public void An_empty_reconciliation_has_no_lines_and_no_mismatches()
    {
        var reconciliation = Reconciliation.Compare([], []);

        reconciliation.Lines.ShouldBeEmpty();
        reconciliation.Mismatches.ShouldBe(0);
        reconciliation.ByField().ShouldBeEmpty();
    }

    private static ReconciledReturn Return(ReconciliationKey key, params (string Field, decimal? Value)[] values) =>
        new(key, values.ToDictionary(v => v.Field, v => v.Value, StringComparer.Ordinal));
}
