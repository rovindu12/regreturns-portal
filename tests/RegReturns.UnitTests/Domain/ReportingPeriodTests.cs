using RegReturns.Domain.Common;
using RegReturns.Domain.Periods;

namespace RegReturns.UnitTests.Domain;

public sealed class ReportingPeriodTests
{
    [Fact]
    public void Monthly_period_has_calendar_month_bounds_and_label()
    {
        var period = ReportingPeriod.Monthly(2024, 2);

        period.Start.ShouldBe(new DateOnly(2024, 2, 1));
        period.End.ShouldBe(new DateOnly(2024, 2, 29));
        period.Label.ShouldBe("2024-02");
    }

    [Theory]
    [InlineData(1, "2026-01-01", "2026-03-31")]
    [InlineData(4, "2026-10-01", "2026-12-31")]
    public void Quarterly_period_has_calendar_quarter_bounds(int quarter, string start, string end)
    {
        var period = ReportingPeriod.Quarterly(2026, quarter);

        period.Start.ShouldBe(DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture));
        period.End.ShouldBe(DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture));
        period.Label.ShouldBe($"2026-Q{quarter}");
    }

    [Fact]
    public void Previous_and_next_cross_year_boundaries()
    {
        ReportingPeriod.Monthly(2026, 1).Previous().ShouldBe(ReportingPeriod.Monthly(2025, 12));
        ReportingPeriod.Quarterly(2025, 4).Next().ShouldBe(ReportingPeriod.Quarterly(2026, 1));
        ReportingPeriod.Quarterly(2026, 2).SamePeriodLastYear().ShouldBe(ReportingPeriod.Quarterly(2025, 2));
    }

    [Theory]
    [InlineData("2026-08-15", ReturnFrequency.Monthly, "2026-08")]
    [InlineData("2026-08-15", ReturnFrequency.Quarterly, "2026-Q3")]
    public void Containing_returns_the_period_that_holds_a_date(string date, ReturnFrequency frequency, string expected)
    {
        var period = ReportingPeriod.Containing(frequency, DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));

        period.Label.ShouldBe(expected);
    }

    [Theory]
    [InlineData("2026-03", true)]
    [InlineData("2026-Q2", true)]
    [InlineData("2026-13", false)]
    [InlineData("2026-Q5", false)]
    [InlineData("26-03", false)]
    [InlineData(null, false)]
    public void TryParse_round_trips_labels_and_rejects_invalid_input(string? label, bool valid)
    {
        ReportingPeriod.TryParse(label, out var period).ShouldBe(valid);
        if (valid)
        {
            period!.Label.ShouldBe(label);
        }
    }

    [Fact]
    public void Invalid_period_numbers_are_rejected()
    {
        Should.Throw<DomainException>(() => ReportingPeriod.Monthly(2026, 13));
        Should.Throw<DomainException>(() => ReportingPeriod.Quarterly(2026, 0));
    }

    [Fact]
    public void Periods_compare_by_start_date()
    {
        (ReportingPeriod.Monthly(2026, 1) < ReportingPeriod.Monthly(2026, 2)).ShouldBeTrue();
        (ReportingPeriod.Monthly(2026, 3) >= ReportingPeriod.Monthly(2026, 3)).ShouldBeTrue();
        ReportingPeriod.Monthly(2026, 3).Equals(ReportingPeriod.Monthly(2026, 3)).ShouldBeTrue();
    }
}
