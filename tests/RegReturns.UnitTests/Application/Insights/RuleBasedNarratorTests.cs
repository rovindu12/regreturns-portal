using RegReturns.Application.Insights;
using RegReturns.Domain.Insights;
using RegReturns.Domain.Templates;
using RegReturns.UnitTests.TestSupport;

namespace RegReturns.UnitTests.Application.Insights;

public sealed class RuleBasedNarratorTests
{
    private readonly InsightRequest _request = new InsightWorld().Request();

    [Fact]
    public void The_same_payload_always_gives_the_same_text()
    {
        RuleBasedNarrator.Write(_request).ShouldBeEquivalentTo(RuleBasedNarrator.Write(new InsightWorld().Request()));
    }

    [Fact]
    public void Headline_names_the_largest_movement_and_the_findings()
    {
        RuleBasedNarrator.Write(_request).Headline.ShouldBe(
            "Non-performing loan ratio rose by 150% against 2026-01, the largest movement in this return; 2 warnings, 1 not justified.");
    }

    [Fact]
    public void Each_failed_rule_gets_an_observation_saying_whether_the_bank_justified_it()
    {
        var observations = RuleBasedNarrator.Write(_request).Observations;

        observations[0].ShouldStartWith("Non-performing loan ratio is outside its supervisory limit (Non-performing loan ratio above 10%).");
        observations[0].ShouldEndWith("The bank has not justified this warning.");
        observations[1].ShouldStartWith("Non-performing loan ratio rose by 150% against 2026-01 (Non-performing loan ratio moved by more than 25%).");
        observations[1].ShouldEndWith("The bank justified this warning.");
    }

    [Fact]
    public void A_mover_no_rule_flagged_is_pointed_out()
    {
        RuleBasedNarrator.Write(_request).Observations[2].ShouldBe(
            "Total deposits rose by 20.1% against 2026-01 without triggering a rule; check whether the bank explains it elsewhere.");
    }

    [Fact]
    public void There_is_one_question_per_field()
    {
        RuleBasedNarrator.Write(_request).Questions.ShouldBe(
        [
            "What is the bank doing to bring non-performing loan ratio back within the supervisory limit, and by when?",
            "What explains the rise in total deposits since 2026-01, and is it expected to last?",
        ]);
    }

    [Fact]
    public void A_quiet_return_says_that_nothing_stands_out()
    {
        var narrative = RuleBasedNarrator.Write(InsightWorld.RequestOf([InsightWorld.Field("A", 101m, 100m)]));

        narrative.Headline.ShouldBe("No figure moved by 10% or more against the earlier periods; no validation findings.");
        narrative.Observations.ShouldHaveSingleItem().ShouldStartWith("Every figure is within 10%");
        narrative.Questions.ShouldBeEmpty();
    }

    [Fact]
    public void An_error_says_the_return_cannot_be_submitted_as_it_is()
    {
        var request = InsightWorld.RequestOf(
            [InsightWorld.Field("TOTAL", 90m, label: "Total")],
            new InsightFinding("TOTAL_SUM", RuleType.CrossField, Severity.Error, "TOTAL", "Total must equal the sum of its parts.", false));

        var narrative = RuleBasedNarrator.Write(request);

        narrative.Headline.ShouldEndWith("1 validation error still stands against it.");
        narrative.Observations.ShouldHaveSingleItem().ShouldEndWith("It is an error, so the return cannot be submitted as it is.");
        narrative.Questions.ShouldBe(["How does the bank derive total from its components, and which figure is correct?"]);
    }

    [Fact]
    public void A_change_against_last_year_says_so()
    {
        var narrative = RuleBasedNarrator.Write(InsightWorld.RequestOf([InsightWorld.Field("Deposits", 80m, previous: null, lastYear: 100m)]));

        narrative.Headline.ShouldStartWith("Deposits fell by 20% against 2025-02 (a year earlier)");
    }

    [Fact]
    public async Task Narrate_answers_with_the_rule_set_and_no_tokens()
    {
        var narrator = new RuleBasedNarrator();

        var answer = (await narrator.NarrateAsync(_request, "{}", TestContext.Current.CancellationToken)).Value;

        (narrator.Provider, narrator.IsConfigured).ShouldBe((InsightProvider.RuleBased, true));
        (answer.Model, answer.InputTokens, answer.OutputTokens).ShouldBe((RuleBasedNarrator.RuleSet, 0L, 0L));
        answer.Narrative.ShouldBeEquivalentTo(RuleBasedNarrator.Write(_request));
    }
}
