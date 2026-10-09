using RegReturns.Web.Models.Demo;

namespace RegReturns.UnitTests.Web.Demo;

public sealed class DemoBannerViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 14, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(30, "in under a minute")]
    [InlineData(60, "in 1 minute")]
    [InlineData(59 * 60, "in 59 minutes")]
    [InlineData(60 * 60, "in 1 hour")]
    [InlineData((13 * 60 * 60) + (59 * 60), "in 13 hours")]
    public void Says_how_long_until_the_next_reset(int seconds, string expected)
    {
        new DemoBannerViewModel(Now.AddSeconds(seconds), Now).Countdown.ShouldBe(expected);
    }

    [Fact]
    public void Says_nothing_when_no_reset_is_scheduled()
    {
        new DemoBannerViewModel(null, Now).Countdown.ShouldBeNull();
    }
}
