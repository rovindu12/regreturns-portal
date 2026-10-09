using RegReturns.Domain.Submissions;

namespace RegReturns.UnitTests.Domain;

public sealed class MigratedFilingTests
{
    private static readonly DateOnly PeriodEnd = new(2024, 1, 31);
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_return_filed_on_the_last_day_of_its_period_is_in_order()
    {
        var filed = new DateTimeOffset(2024, 1, 31, 0, 0, 0, TimeSpan.Zero);

        Filing(filed, filed.AddDays(3)).IsInOrder(PeriodEnd, Now).ShouldBeTrue();
    }

    [Fact]
    public void A_return_filed_before_its_period_ended_is_out_of_order()
    {
        var filed = new DateTimeOffset(2024, 1, 30, 23, 59, 59, TimeSpan.Zero);

        Filing(filed, filed.AddDays(3)).IsInOrder(PeriodEnd, Now).ShouldBeFalse();
    }

    [Fact]
    public void The_filing_date_is_judged_in_utc()
    {
        // 1 February 01:30 in Valoria at UTC+2 is still 31 January in UTC.
        var filed = new DateTimeOffset(2024, 2, 1, 1, 30, 0, TimeSpan.FromHours(2));
        var beforeTheEnd = new DateTimeOffset(2024, 1, 31, 1, 30, 0, TimeSpan.FromHours(2));

        Filing(filed, filed.AddDays(1)).IsInOrder(PeriodEnd, Now).ShouldBeTrue();
        Filing(beforeTheEnd, filed.AddDays(1)).IsInOrder(PeriodEnd, Now).ShouldBeFalse();
    }

    [Fact]
    public void Approval_at_the_moment_of_filing_is_in_order()
    {
        var filed = new DateTimeOffset(2024, 2, 8, 9, 0, 0, TimeSpan.Zero);

        Filing(filed, filed).IsInOrder(PeriodEnd, Now).ShouldBeTrue();
    }

    [Fact]
    public void Approval_before_filing_is_out_of_order()
    {
        var filed = new DateTimeOffset(2024, 2, 8, 9, 0, 0, TimeSpan.Zero);

        Filing(filed, filed.AddSeconds(-1)).IsInOrder(PeriodEnd, Now).ShouldBeFalse();
    }

    [Fact]
    public void Approval_up_to_now_is_in_order()
    {
        Filing(Now.AddDays(-1), Now).IsInOrder(PeriodEnd, Now).ShouldBeTrue();
    }

    [Fact]
    public void Approval_in_the_future_is_out_of_order()
    {
        Filing(Now.AddDays(-1), Now.AddTicks(1)).IsInOrder(PeriodEnd, Now).ShouldBeFalse();
    }

    private static MigratedFiling Filing(DateTimeOffset filed, DateTimeOffset approved) =>
        new(filed, approved, "Migrated by run 1.", "Accepted in the legacy system without a justification.");
}
