using RegReturns.Application.Insights;
using RegReturns.Domain.Insights;
using RegReturns.Domain.Templates;
using RegReturns.UnitTests.TestSupport;
using RegReturns.Web.Models.Supervision;

namespace RegReturns.UnitTests.Web;

public sealed class ReturnInsightViewModelTests
{
    private static ReturnInsightViewModel Model(
        InsightProvider configured = InsightProvider.Anthropic,
        bool keySet = true,
        InsightProvider? writtenBy = null,
        InsightFallbackReason? reason = null)
    {
        var world = new InsightWorld();
        var request = world.Request();
        var payload = InsightJson.Serialize(request);
        ReturnInsightView? latest = null;
        if (writtenBy is { } provider)
        {
            latest = new ReturnInsightView(
                Guid.CreateVersion7(),
                1,
                InsightWorld.Now,
                provider,
                provider == InsightProvider.Anthropic ? "claude-opus-5-5" : RuleBasedNarrator.RuleSet,
                reason,
                InsightContent.Compose(request, RuleBasedNarrator.Write(request)),
                payload,
                ReturnInsight.Digest(payload),
                new string('0', 64),
                8100,
                42);
        }

        return new ReturnInsightViewModel(Guid.CreateVersion7(), new ReturnInsightPanel(1, configured, "claude-opus-5-5", keySet, latest));
    }

    [Theory]
    [InlineData(1234.5, "VLD m", "1,234.5 VLD m")]
    [InlineData(12.5, "%", "12.5%")]
    [InlineData(42, "", "42")]
    [InlineData(-0.25, "VLD m", "-0.25 VLD m")]
    public void Figures_carry_their_unit(double value, string unit, string expected)
    {
        ReturnInsightViewModel.Figure((decimal)value, unit).ShouldBe(expected);
    }

    [Theory]
    [InlineData(147.2, "+147.2%")]
    [InlineData(-12, "-12%")]
    [InlineData(0, "0%")]
    public void Changes_carry_their_sign(double change, string expected)
    {
        ReturnInsightViewModel.Change((decimal)change).ShouldBe(expected);
    }

    [Theory]
    [InlineData(850, "850 ms")]
    [InlineData(8100, "8.1 s")]
    public void Durations_read_naturally(int milliseconds, string expected)
    {
        ReturnInsightViewModel.Duration(milliseconds).ShouldBe(expected);
    }

    [Fact]
    public void Bases_are_named_in_words()
    {
        ReturnInsightViewModel.Basis(VarianceBasis.PreviousPeriod).ShouldBe("previous period");
        ReturnInsightViewModel.Basis(VarianceBasis.SamePeriodLastYear).ShouldBe("same period last year");
    }

    [Fact]
    public void The_note_says_whether_anything_leaves_the_portal()
    {
        Model().ProviderNote.ShouldContain("through the Anthropic API");
        Model(keySet: false).ProviderNote.ShouldContain("nothing leaves the portal");
        Model(InsightProvider.RuleBased).ProviderNote.ShouldContain("nothing leaves the portal");
    }

    [Fact]
    public void Without_an_insight_the_button_generates_one()
    {
        var model = Model();

        (model.Latest, model.ButtonLabel, model.PayloadSent, model.SharedNote).ShouldBe((null, "Generate insight", false, string.Empty));
    }

    [Fact]
    public void An_ai_insight_was_sent_and_is_labelled_as_ai_written()
    {
        var model = Model(writtenBy: InsightProvider.Anthropic);

        (model.IsAiWritten, model.PayloadSent, model.ButtonLabel, model.WrittenBy).ShouldBe((true, true, "Refresh insight", "Claude (claude-opus-5-5)"));
        model.SharedNote.ShouldStartWith("This exact document was sent");
    }

    [Theory]
    [InlineData(InsightFallbackReason.NotConfigured, false)]
    [InlineData(InsightFallbackReason.PayloadRejected, false)]
    [InlineData(InsightFallbackReason.Timeout, true)]
    [InlineData(InsightFallbackReason.Refused, true)]
    public void A_fallback_was_sent_only_when_the_provider_was_asked(InsightFallbackReason reason, bool sent)
    {
        var model = Model(writtenBy: InsightProvider.RuleBased, reason: reason);

        (model.IsAiWritten, model.PayloadSent, model.WrittenBy).ShouldBe((false, sent, "fixed rules"));
        model.SharedNote.ShouldStartWith(sent ? "This exact document was sent" : "Nothing was sent");
    }

    [Fact]
    public void A_rule_based_insight_sent_nothing()
    {
        Model(InsightProvider.RuleBased, writtenBy: InsightProvider.RuleBased).PayloadSent.ShouldBeFalse();
    }

    [Fact]
    public void The_payload_is_shown_indented_without_escaping()
    {
        var shown = Model(writtenBy: InsightProvider.Anthropic).PayloadForDisplay;

        shown.ShouldContain("\n  \"schema\": \"regreturns.insight-request/1\"");
        shown.ShouldContain("Non-performing loan ratio above 10%.");
    }
}
