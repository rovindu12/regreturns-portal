using RegReturns.Application.Insights;

namespace RegReturns.UnitTests.Application.Insights;

public sealed class InsightNarrativeTests
{
    [Fact]
    public void Clean_collapses_white_space_and_control_characters()
    {
        var narrative = InsightNarrative.Clean("  Deposits\r\n\trose \u0007 sharply. ", ["a\n\nb"], []).Value;

        narrative.Headline.ShouldBe("Deposits rose sharply.");
        narrative.Observations.ShouldBe(["a b"]);
    }

    [Fact]
    public void Clean_drops_blank_items_and_keeps_at_most_five_of_each()
    {
        var items = new[] { "1", " ", null, "2", "3", "4", "5", "6" };

        var narrative = InsightNarrative.Clean("Headline", items, items).Value;

        narrative.Observations.ShouldBe(["1", "2", "3", "4", "5"]);
        narrative.Questions.ShouldBe(["1", "2", "3", "4", "5"]);
    }

    [Fact]
    public void Clean_shortens_long_text_at_a_word_boundary()
    {
        var headline = string.Join(' ', Enumerable.Repeat("liquidity", 60));

        var cleaned = InsightNarrative.Clean(headline, null, null).Value.Headline;

        cleaned.Length.ShouldBeLessThanOrEqualTo(InsightNarrative.HeadlineMaxLength);
        cleaned.ShouldEndWith("liquidity…");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \n ")]
    public void Clean_needs_a_headline(string? headline)
    {
        InsightNarrative.Clean(headline, ["a"], ["b"]).Error.ShouldBe(InsightNarrative.MissingHeadline);
    }
}
