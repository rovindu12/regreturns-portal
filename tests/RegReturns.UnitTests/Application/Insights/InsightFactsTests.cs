using RegReturns.Application.Insights;
using RegReturns.Domain.Templates;
using RegReturns.UnitTests.TestSupport;

namespace RegReturns.UnitTests.Application.Insights;

public sealed class InsightFactsTests
{
    [Fact]
    public void Movers_are_changes_of_at_least_ten_percent_largest_first()
    {
        var movers = InsightFacts.Movers(new InsightWorld().Request());

        movers.Select(m => (m.FieldCode, m.ChangePercent)).ShouldBe([("NPL_RATIO", 150m), ("DEPOSITS", 20.1m)]);
        movers[0].ShouldBe(new InsightMover("NPL_RATIO", "Non-performing loan ratio", "%", 12.5m, 5m, 150m, VarianceBasis.PreviousPeriod));
    }

    [Fact]
    public void A_fall_counts_as_much_as_a_rise()
    {
        var request = InsightWorld.RequestOf([InsightWorld.Field("A", 90m, 100m), InsightWorld.Field("B", 50m, 100m)]);

        InsightFacts.Movers(request).Select(m => m.FieldCode).ShouldBe(["B", "A"]);
    }

    [Fact]
    public void Movers_compare_with_last_year_when_there_is_no_previous_figure()
    {
        var request = InsightWorld.RequestOf([InsightWorld.Field("A", 150m, previous: null, lastYear: 100m)]);

        var mover = InsightFacts.Movers(request).ShouldHaveSingleItem();

        (mover.Prior, mover.ChangePercent, mover.Basis).ShouldBe((100m, 50m, VarianceBasis.SamePeriodLastYear));
    }

    [Fact]
    public void Movers_list_at_most_five_and_ties_keep_field_order()
    {
        var fields = Enumerable.Range(1, 7).Select(i => InsightWorld.Field($"F{i}", 200m, 100m)).ToList();

        InsightFacts.Movers(InsightWorld.RequestOf(fields)).Select(m => m.FieldCode).ShouldBe(["F1", "F2", "F3", "F4", "F5"]);
    }

    [Fact]
    public void Breaches_carry_the_field_label_and_the_rule_text()
    {
        var breaches = InsightFacts.Breaches(new InsightWorld().Request());

        breaches.Select(b => (b.RuleCode, b.FieldLabel, b.JustifiedByBank)).ShouldBe(
        [
            ("NPL_MAX", "Non-performing loan ratio", false),
            ("NPL_VAR", "Non-performing loan ratio", true),
        ]);
    }

    [Fact]
    public void Content_round_trips_through_its_json()
    {
        var world = new InsightWorld();
        var content = InsightContent.Compose(world.Request(), RuleBasedNarrator.Write(world.Request()));

        var read = InsightContent.FromJson(content.ToJson());

        read.ToJson().ShouldBe(content.ToJson());
        read.Schema.ShouldBe(InsightContent.CurrentSchema);
        read.Movers.Count.ShouldBe(2);
    }
}
