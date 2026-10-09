using RegReturns.Application.Demo;

namespace RegReturns.UnitTests.Application.Demo;

public sealed class DemoErrorsTests
{
    private static readonly DateTimeOffset LastReset = new(2026, 10, 9, 14, 3, 0, TimeSpan.Zero);

    [Fact]
    public void A_reset_is_available_again_after_the_cooldown()
    {
        DemoErrors.AvailableAt(LastReset, TimeSpan.FromMinutes(10)).ShouldBe(LastReset.AddMinutes(10));
    }

    [Fact]
    public void Without_an_earlier_reset_there_is_no_cooldown()
    {
        DemoErrors.AvailableAt(null, TimeSpan.FromMinutes(10)).ShouldBeNull();
    }

    [Fact]
    public void Cooling_down_keeps_its_code_and_says_when_in_utc()
    {
        var error = DemoErrors.CoolingDownUntil(new DateTimeOffset(2026, 10, 9, 16, 13, 0, TimeSpan.FromHours(2)));

        error.Code.ShouldBe(DemoErrors.CoolingDown.Code);
        error.Message.ShouldEndWith("It can be reset again from 14:13 UTC.");
    }
}
