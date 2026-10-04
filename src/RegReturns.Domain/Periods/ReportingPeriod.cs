using System.Globalization;

using RegReturns.Domain.Common;

namespace RegReturns.Domain.Periods;

/// <summary>
/// A calendar month or quarter that a return reports on. Immutable value object.
/// </summary>
public sealed class ReportingPeriod : IEquatable<ReportingPeriod>, IComparable<ReportingPeriod>
{
    private const int MonthsPerQuarter = 3;

    // Required by EF Core to materialise the complex type.
    private ReportingPeriod()
    {
    }

    private ReportingPeriod(ReturnFrequency frequency, int year, int number)
    {
        var max = frequency == ReturnFrequency.Monthly ? 12 : 4;
        if (year is < 2000 or > 2100)
        {
            throw new DomainException($"Year {year} is outside the supported range 2000-2100.");
        }

        if (number < 1 || number > max)
        {
            throw new DomainException($"Period number {number} is invalid for a {frequency} return (1-{max}).");
        }

        Frequency = frequency;
        Year = year;
        Number = number;
    }

    /// <summary>Gets the frequency.</summary>
    public ReturnFrequency Frequency { get; private set; }

    /// <summary>Gets the calendar year.</summary>
    public int Year { get; private set; }

    /// <summary>Gets the month (1-12) or quarter (1-4).</summary>
    public int Number { get; private set; }

    /// <summary>Gets the first day of the period.</summary>
    public DateOnly Start => new(Year, FirstMonth, 1);

    /// <summary>Gets the last day of the period.</summary>
    public DateOnly End => Start.AddMonths(Frequency == ReturnFrequency.Monthly ? 1 : MonthsPerQuarter).AddDays(-1);

    /// <summary>Gets a sortable label such as <c>2026-03</c> or <c>2026-Q1</c>.</summary>
    public string Label => Frequency == ReturnFrequency.Monthly
        ? string.Create(CultureInfo.InvariantCulture, $"{Year}-{Number:00}")
        : string.Create(CultureInfo.InvariantCulture, $"{Year}-Q{Number}");

    private int FirstMonth => Frequency == ReturnFrequency.Monthly ? Number : ((Number - 1) * MonthsPerQuarter) + 1;

    /// <summary>Creates a monthly period.</summary>
    /// <param name="year">The year.</param>
    /// <param name="month">The month (1-12).</param>
    /// <returns>The period.</returns>
    public static ReportingPeriod Monthly(int year, int month) => new(ReturnFrequency.Monthly, year, month);

    /// <summary>Creates a quarterly period.</summary>
    /// <param name="year">The year.</param>
    /// <param name="quarter">The quarter (1-4).</param>
    /// <returns>The period.</returns>
    public static ReportingPeriod Quarterly(int year, int quarter) => new(ReturnFrequency.Quarterly, year, quarter);

    /// <summary>Returns the period of the given frequency that contains a date.</summary>
    /// <param name="frequency">The frequency.</param>
    /// <param name="date">Any date in the period.</param>
    /// <returns>The containing period.</returns>
    public static ReportingPeriod Containing(ReturnFrequency frequency, DateOnly date) =>
        frequency == ReturnFrequency.Monthly
            ? Monthly(date.Year, date.Month)
            : Quarterly(date.Year, ((date.Month - 1) / MonthsPerQuarter) + 1);

    /// <summary>Parses a label produced by <see cref="Label"/>.</summary>
    /// <param name="label">For example <c>2026-03</c> or <c>2026-Q1</c>.</param>
    /// <param name="period">The parsed period.</param>
    /// <returns><see langword="true"/> if the label was valid.</returns>
    public static bool TryParse(string? label, out ReportingPeriod? period)
    {
        period = null;
        if (label is not { Length: 7 } || label[4] != '-'
            || !int.TryParse(label.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year))
        {
            return false;
        }

        var isQuarter = label[5] == 'Q';
        var numberSpan = isQuarter ? label.AsSpan(6, 1) : label.AsSpan(5, 2);
        if (!int.TryParse(numberSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || year is < 2000 or > 2100
            || number < 1 || number > (isQuarter ? 4 : 12))
        {
            return false;
        }

        period = isQuarter ? Quarterly(year, number) : Monthly(year, number);
        return true;
    }

    /// <summary>Returns the immediately preceding period of the same frequency.</summary>
    /// <returns>The previous period.</returns>
    public ReportingPeriod Previous() => Containing(Frequency, Start.AddDays(-1));

    /// <summary>Returns the immediately following period of the same frequency.</summary>
    /// <returns>The next period.</returns>
    public ReportingPeriod Next() => Containing(Frequency, End.AddDays(1));

    /// <summary>Returns the same period one year earlier.</summary>
    /// <returns>The same period in the previous year.</returns>
    public ReportingPeriod SamePeriodLastYear() => new(Frequency, Year - 1, Number);

    /// <inheritdoc />
    public bool Equals(ReportingPeriod? other) =>
        other is not null && Frequency == other.Frequency && Year == other.Year && Number == other.Number;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ReportingPeriod other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Frequency, Year, Number);

    /// <inheritdoc />
    public int CompareTo(ReportingPeriod? other)
    {
        if (other is null)
        {
            return 1;
        }

        var byFrequency = Frequency.CompareTo(other.Frequency);
        return byFrequency != 0 ? byFrequency : Start.CompareTo(other.Start);
    }

    /// <inheritdoc />
    public override string ToString() => Label;

    /// <summary>Equality operator.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>Whether the periods are equal.</returns>
    public static bool operator ==(ReportingPeriod? left, ReportingPeriod? right) => Equals(left, right);

    /// <summary>Inequality operator.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>Whether the periods differ.</returns>
    public static bool operator !=(ReportingPeriod? left, ReportingPeriod? right) => !Equals(left, right);

    /// <summary>Less-than operator.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>Whether <paramref name="left"/> is earlier.</returns>
    public static bool operator <(ReportingPeriod left, ReportingPeriod right) =>
        left is null ? right is not null : left.CompareTo(right) < 0;

    /// <summary>Greater-than operator.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>Whether <paramref name="left"/> is later.</returns>
    public static bool operator >(ReportingPeriod left, ReportingPeriod right) =>
        left is not null && left.CompareTo(right) > 0;

    /// <summary>Less-than-or-equal operator.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>Whether <paramref name="left"/> is earlier or equal.</returns>
    public static bool operator <=(ReportingPeriod left, ReportingPeriod right) =>
        left is null || left.CompareTo(right) <= 0;

    /// <summary>Greater-than-or-equal operator.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>Whether <paramref name="left"/> is later or equal.</returns>
    public static bool operator >=(ReportingPeriod left, ReportingPeriod right) =>
        left is null ? right is null : left.CompareTo(right) >= 0;
}
