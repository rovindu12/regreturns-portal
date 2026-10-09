using Microsoft.Extensions.Options;

using RegReturns.Application.Demo;
using RegReturns.Infrastructure.Demo;

namespace RegReturns.UnitTests.Infrastructure.Demo;

public sealed class CronDemoResetScheduleTests
{
    private static readonly DateTimeOffset Afternoon = new(2026, 10, 9, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public void The_nightly_schedule_resets_at_three_in_the_morning_utc()
    {
        var schedule = Schedule(enabled: true, "0 3 * * *");

        schedule.Expression.ShouldBe("0 3 * * *");
        schedule.NextAfter(Afternoon).ShouldBe(new DateTimeOffset(2026, 10, 10, 3, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void An_instant_in_another_offset_is_read_as_the_same_moment()
    {
        var schedule = Schedule(enabled: true, "0 3 * * *");

        // 04:30 in UTC+2 is 02:30 UTC, half an hour before the reset.
        schedule.NextAfter(new DateTimeOffset(2026, 10, 9, 4, 30, 0, TimeSpan.FromHours(2)))!.Value.UtcDateTime
            .ShouldBe(new DateTime(2026, 10, 9, 3, 0, 0, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData(false, "0 3 * * *")]
    [InlineData(true, "")]
    [InlineData(true, "  ")]
    [InlineData(true, null)]
    public void Off_outside_demo_mode_or_without_an_expression(bool enabled, string? expression)
    {
        var schedule = Schedule(enabled, expression);

        schedule.Expression.ShouldBeNull();
        schedule.NextAfter(Afternoon).ShouldBeNull();
    }

    [Theory]
    [InlineData("0 3 * * *", true)]
    [InlineData("*/30 * * * *", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("every night", false)]
    [InlineData("0 0 3 * * *", false)]
    public void Validates_five_field_expressions(string? expression, bool valid)
    {
        CronDemoResetSchedule.IsValid(expression).ShouldBe(valid);
    }

    private static CronDemoResetSchedule Schedule(bool enabled, string? expression) =>
        new(Options.Create(new DemoOptions { Enabled = enabled, ResetSchedule = expression }));
}
