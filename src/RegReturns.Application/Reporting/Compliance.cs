using System.ComponentModel.DataAnnotations;

using RegReturns.Domain.Obligations;
using RegReturns.Domain.Periods;

namespace RegReturns.Application.Reporting;

/// <summary>Where a filing obligation stands for compliance (ADR 0028).</summary>
public enum ComplianceState
{
    /// <summary>The institution had no obligation for the period.</summary>
    NoObligation = 0,

    /// <summary>Not filed yet, and the due date has not passed.</summary>
    NotDue = 1,

    /// <summary>First submitted on or before the due date.</summary>
    OnTime = 2,

    /// <summary>First submitted after the due date.</summary>
    Late = 3,

    /// <summary>Past the due date with nothing on file: never submitted, or rejected and not filed again.</summary>
    Overdue = 4,
}

/// <summary>The compliance rules every dashboard, report and export uses.</summary>
public static class Compliance
{
    /// <summary>
    /// Classifies an obligation on a day. An open obligation (never submitted, or its return was rejected) is overdue
    /// once its due date has passed; any other was submitted, on time or late by its first submission.
    /// </summary>
    /// <param name="status">The obligation's status.</param>
    /// <param name="isLate">Whether its first submission was after the due date.</param>
    /// <param name="dueDate">The due date.</param>
    /// <param name="today">The day to judge on (UTC).</param>
    /// <returns>The state.</returns>
    public static ComplianceState StateOf(ObligationStatus status, bool isLate, DateOnly dueDate, DateOnly today) =>
        (status, isLate) switch
        {
            (ObligationStatus.Open, _) when today > dueDate => ComplianceState.Overdue,
            (ObligationStatus.Open, _) => ComplianceState.NotDue,
            (_, true) => ComplianceState.Late,
            _ => ComplianceState.OnTime,
        };

    /// <summary>Returns how many days an obligation is overdue on a day, or 0 when it is not past its due date.</summary>
    /// <param name="dueDate">The due date.</param>
    /// <param name="today">The day (UTC).</param>
    /// <returns>The days past the due date.</returns>
    public static int DaysOverdue(DateOnly dueDate, DateOnly today) => Math.Max(0, today.DayNumber - dueDate.DayNumber);

    /// <summary>
    /// Returns the periods a dashboard shows for a frequency: the last <paramref name="count"/> completed periods up
    /// to the one before the period containing <paramref name="today"/>, oldest first.
    /// </summary>
    /// <param name="frequency">Monthly or quarterly.</param>
    /// <param name="today">The day (UTC).</param>
    /// <param name="count">How many periods.</param>
    /// <returns>The periods, oldest first.</returns>
    public static IReadOnlyList<ReportingPeriod> Window(ReturnFrequency frequency, DateOnly today, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        var periods = new ReportingPeriod[count];
        var period = ReportingPeriod.Containing(frequency, today).Previous();
        for (var i = count - 1; i >= 0; i--)
        {
            periods[i] = period;
            period = period.Previous();
        }

        return periods;
    }
}

/// <summary>Settings for dashboards and reports (section <c>Reports</c>).</summary>
public sealed class ReportingOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Reports";

    /// <summary>Gets or sets how many months a monthly return's dashboard shows.</summary>
    [Range(3, 36)]
    public int MonthsShown { get; set; } = 12;

    /// <summary>Gets or sets how many quarters a quarterly return's dashboard shows.</summary>
    [Range(2, 12)]
    public int QuartersShown { get; set; } = 4;

    /// <summary>
    /// Gets the key ratios whose trends the dashboard shows, one field of one return type each. Empty by default (the
    /// values live in appsettings.json, as configuration binding appends to a list's initial items).
    /// </summary>
    public IList<KeyRatioSetting> KeyRatios { get; } = [];

    /// <summary>Returns how many periods of a frequency the dashboards show.</summary>
    /// <param name="frequency">Monthly or quarterly.</param>
    /// <returns>The number of periods.</returns>
    public int PeriodsShown(ReturnFrequency frequency) => frequency == ReturnFrequency.Monthly ? MonthsShown : QuartersShown;
}

/// <summary>A key ratio shown as a trend on the dashboard.</summary>
public sealed class KeyRatioSetting
{
    /// <summary>Gets or sets the return type code, such as <c>MLR</c>.</summary>
    public string ReturnType { get; set; } = string.Empty;

    /// <summary>Gets or sets the field code, such as <c>LCR</c>.</summary>
    public string Field { get; set; } = string.Empty;
}
