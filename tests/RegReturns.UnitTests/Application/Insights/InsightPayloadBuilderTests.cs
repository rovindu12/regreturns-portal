using RegReturns.Application.Insights;
using RegReturns.Domain.Insights;
using RegReturns.Domain.Templates;
using RegReturns.UnitTests.TestSupport;

namespace RegReturns.UnitTests.Application.Insights;

public sealed class InsightPayloadBuilderTests
{
    private readonly InsightWorld _world = new();

    [Fact]
    public void Payload_holds_only_numeric_fields_in_display_order()
    {
        _world.Request().Fields.Select(f => f.Code).ShouldBe(["DEPOSITS", "LOANS", "NPL_RATIO", "BRANCHES"]);
    }

    [Fact]
    public void Payload_carries_no_bank_people_justifications_or_text_values()
    {
        var payload = _world.Payload();

        payload.ShouldNotContain(InsightWorld.BankName);
        payload.ShouldNotContain($"\"{InsightWorld.BankCode}\"");
        payload.ShouldNotContain(InsightWorld.Remarks);
        payload.ShouldNotContain(InsightWorld.Justification);
        payload.ShouldNotContain(InsightWorld.MakerEmail);
        payload.ShouldNotContain("Mia Maker");
        payload.ShouldNotContain("2026-02-28");
        payload.ShouldNotContain(_world.Submission.Id.ToString());
    }

    [Fact]
    public void Payload_names_the_return_type_and_periods()
    {
        var request = _world.Request();

        request.Schema.ShouldBe(InsightRequest.CurrentSchema);
        request.ReturnType.ShouldBe(new InsightReturnType("MDA", "Monthly Deposits and Advances Return"));
        (request.Period, request.PreviousPeriod, request.SamePeriodLastYear).ShouldBe(("2026-02", "2026-01", "2025-02"));
        request.Revision.ShouldBe(1);
    }

    [Fact]
    public void Changes_are_computed_against_both_earlier_periods()
    {
        var ratio = _world.Request().FindField("NPL_RATIO").ShouldNotBeNull();

        ratio.Current.ShouldBe(12.5m);
        ratio.PreviousPeriod.ShouldBe(5m);
        ratio.SamePeriodLastYear.ShouldBe(4m);
        ratio.ChangeVsPreviousPercent.ShouldBe(150m);
        ratio.ChangeVsLastYearPercent.ShouldBe(212.5m);
    }

    [Fact]
    public void A_field_without_earlier_figures_has_no_change()
    {
        var branches = _world.Request().FindField("BRANCHES").ShouldNotBeNull();

        branches.Current.ShouldBe(42m);
        branches.PreviousPeriod.ShouldBeNull();
        branches.ChangeVsPreviousPercent.ShouldBeNull();
    }

    [Theory]
    [InlineData(1200.5, 1000, 20.1)]
    [InlineData(800, 800, 0)]
    [InlineData(-5, -10, 50)]
    [InlineData(90, 100, -10)]
    public void Change_is_in_percent_of_the_absolute_earlier_figure_rounded_to_one_decimal(double current, double before, double expected)
    {
        InsightPayloadBuilder.ChangePercent((decimal)current, (decimal)before).ShouldBe((decimal)expected);
    }

    [Fact]
    public void Change_is_unknown_without_both_figures_or_when_the_earlier_one_is_zero()
    {
        InsightPayloadBuilder.ChangePercent(5m, 0m).ShouldBeNull();
        InsightPayloadBuilder.ChangePercent(null, 5m).ShouldBeNull();
        InsightPayloadBuilder.ChangePercent(5m, null).ShouldBeNull();
    }

    [Fact]
    public void Figures_are_normalised_so_stored_decimals_serialise_the_same_way()
    {
        var payload = _world.Payload();

        payload.ShouldContain("\"current\":1200.5,");
        payload.ShouldContain("\"previousPeriod\":1000,");
    }

    [Fact]
    public void Findings_come_in_field_then_rule_type_order_with_template_text_and_whether_they_were_justified()
    {
        var findings = _world.Request().Findings;

        findings.Select(f => (f.RuleCode, f.RuleType, f.JustifiedByBank)).ShouldBe(
        [
            ("NPL_MAX", RuleType.Range, false),
            ("NPL_VAR", RuleType.Variance, true),
        ]);
        findings[0].Rule.ShouldBe("Non-performing loan ratio above 10%.");
    }

    [Fact]
    public void The_same_return_always_gives_the_same_payload_and_digest()
    {
        ReturnInsight.Digest(_world.Payload()).ShouldBe(ReturnInsight.Digest(new InsightWorld().Payload()));
    }
}
