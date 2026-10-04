using RegReturns.Domain.Common;
using RegReturns.Domain.Obligations;
using RegReturns.Domain.Periods;
using RegReturns.Domain.Templates;

namespace RegReturns.UnitTests.Domain;

public sealed class ReturnObligationTests
{
    private static readonly ReturnType Monthly =
        ReturnType.Create("MLR", "Monthly Liquidity Return", "Liquidity.", ReturnFrequency.Monthly, 15);

    [Fact]
    public void Due_date_is_period_end_plus_due_days()
    {
        var obligation = ReturnObligation.Create(Guid.CreateVersion7(), Monthly, ReportingPeriod.Monthly(2026, 2));

        obligation.DueDate.ShouldBe(new DateOnly(2026, 3, 15));
        obligation.Status.ShouldBe(ObligationStatus.Open);
    }

    [Fact]
    public void Open_obligation_is_overdue_only_after_the_due_date()
    {
        var obligation = ReturnObligation.Create(Guid.CreateVersion7(), Monthly, ReportingPeriod.Monthly(2026, 2));

        obligation.IsOverdue(new DateOnly(2026, 3, 15)).ShouldBeFalse();
        obligation.IsOverdue(new DateOnly(2026, 3, 16)).ShouldBeTrue();

        obligation.MarkSubmitted(new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero));
        obligation.IsOverdue(new DateOnly(2026, 3, 20)).ShouldBeFalse();
        obligation.IsLate.ShouldBeTrue();
    }

    [Fact]
    public void Late_flag_reflects_the_first_submission_only()
    {
        var obligation = ReturnObligation.Create(Guid.CreateVersion7(), Monthly, ReportingPeriod.Monthly(2026, 2));

        obligation.MarkSubmitted(new DateTimeOffset(2026, 3, 14, 10, 0, 0, TimeSpan.Zero));
        obligation.MarkSubmitted(new DateTimeOffset(2026, 3, 30, 10, 0, 0, TimeSpan.Zero));

        obligation.IsLate.ShouldBeFalse();
    }

    [Fact]
    public void Period_frequency_must_match_the_return_type()
    {
        Should.Throw<DomainException>(() =>
            ReturnObligation.Create(Guid.CreateVersion7(), Monthly, ReportingPeriod.Quarterly(2026, 1)));
    }
}
