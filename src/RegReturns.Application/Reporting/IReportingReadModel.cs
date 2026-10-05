using RegReturns.Domain.Obligations;
using RegReturns.Domain.Periods;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;

namespace RegReturns.Application.Reporting;

/// <summary>
/// Reads the aggregates behind the dashboards and reports from the reporting views (ADR 0028). Every method takes the
/// institution to scope to; <see langword="null"/> means all institutions, which only regulator staff may ask for.
/// </summary>
public interface IReportingReadModel
{
    /// <summary>Returns the obligations of active institutions that match every condition of a filter.</summary>
    /// <param name="filter">What to read.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>One row per obligation, with its live return if any.</returns>
    Task<IReadOnlyList<ObligationRow>> GetObligationsAsync(ObligationQuery filter, CancellationToken cancellationToken);

    /// <summary>
    /// Counts the validation findings of submitted revisions by return type, period, rule and severity. A revision
    /// counts once it has been submitted, so a bank's work in progress never shows.
    /// </summary>
    /// <param name="filter">What to count.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>One row per period, rule and severity that has findings.</returns>
    Task<IReadOnlyList<FindingCountRow>> GetFindingCountsAsync(FindingQuery filter, CancellationToken cancellationToken);

    /// <summary>Returns the values of some fields in approved returns, for trends of key ratios.</summary>
    /// <param name="filter">What to read.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>One row per approved return and field that has a numeric value.</returns>
    Task<IReadOnlyList<ApprovedValueRow>> GetApprovedValuesAsync(ApprovedValueQuery filter, CancellationToken cancellationToken);
}

/// <summary>Which obligations to read; every condition that is set applies.</summary>
/// <param name="InstitutionId">Only this institution's, or <see langword="null"/> for all.</param>
/// <param name="ReturnTypeCode">Only this return type's, or <see langword="null"/> for all.</param>
/// <param name="Window">Only periods in this window, or <see langword="null"/> for any period.</param>
/// <param name="OpenDueBefore">Only open obligations (nothing on file) due before this date.</param>
public sealed record ObligationQuery(
    Guid? InstitutionId, string? ReturnTypeCode, PeriodWindow? Window = null, DateOnly? OpenDueBefore = null);

/// <summary>A run of consecutive periods of one frequency.</summary>
public sealed record PeriodWindow
{
    /// <summary>Initializes a new instance of the <see cref="PeriodWindow"/> class.</summary>
    /// <param name="from">The first period.</param>
    /// <param name="to">The last period, of the same frequency and not before <paramref name="from"/>.</param>
    public PeriodWindow(ReportingPeriod from, ReportingPeriod to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if (from.Frequency != to.Frequency || from > to)
        {
            throw new ArgumentException($"{from.Label} to {to.Label} is not a window of periods.", nameof(to));
        }

        From = from;
        To = to;
    }

    /// <summary>Gets the first period.</summary>
    public ReportingPeriod From { get; }

    /// <summary>Gets the last period.</summary>
    public ReportingPeriod To { get; }

    /// <summary>Gets the frequency of the periods.</summary>
    public ReturnFrequency Frequency => From.Frequency;

    /// <summary>Gets the window's periods, oldest first.</summary>
    public IReadOnlyList<ReportingPeriod> Periods
    {
        get
        {
            var periods = new List<ReportingPeriod>();
            for (var period = From; period <= To; period = period.Next())
            {
                periods.Add(period);
            }

            return periods;
        }
    }

    /// <summary>Creates the window running from the first to the last of some periods.</summary>
    /// <param name="periods">The periods, oldest first.</param>
    /// <returns>The window.</returns>
    public static PeriodWindow Of(IReadOnlyList<ReportingPeriod> periods)
    {
        ArgumentNullException.ThrowIfNull(periods);
        return new PeriodWindow(periods[0], periods[^1]);
    }
}

/// <summary>Which findings to count.</summary>
/// <param name="InstitutionId">Only this institution's, or <see langword="null"/> for all.</param>
/// <param name="ReturnTypeCode">The return type.</param>
/// <param name="Window">The periods.</param>
public sealed record FindingQuery(Guid? InstitutionId, string ReturnTypeCode, PeriodWindow Window);

/// <summary>Which approved values to read.</summary>
/// <param name="InstitutionId">Only this institution's, or <see langword="null"/> for all.</param>
/// <param name="ReturnTypeCode">The return type.</param>
/// <param name="FieldCodes">The fields.</param>
/// <param name="Window">The periods.</param>
public sealed record ApprovedValueQuery(Guid? InstitutionId, string ReturnTypeCode, IReadOnlyList<string> FieldCodes, PeriodWindow Window);

/// <summary>An obligation as the reporting view shows it.</summary>
/// <param name="ObligationId">The obligation id.</param>
/// <param name="InstitutionId">The institution id.</param>
/// <param name="InstitutionCode">The institution code.</param>
/// <param name="InstitutionName">The institution name.</param>
/// <param name="ReturnTypeCode">The return type code.</param>
/// <param name="Period">The reporting period.</param>
/// <param name="DueDate">The due date.</param>
/// <param name="Status">Open, in progress or fulfilled.</param>
/// <param name="FirstSubmittedAt">When a return for it was first submitted.</param>
/// <param name="IsLate">Whether that was after the due date.</param>
/// <param name="SubmissionStatus">The status of its live (not rejected) return, if one was started.</param>
/// <param name="SubmissionFirstSubmittedAt">When that return was first submitted (regulator staff only see it from then).</param>
public sealed record ObligationRow(
    Guid ObligationId,
    Guid InstitutionId,
    string InstitutionCode,
    string InstitutionName,
    string ReturnTypeCode,
    ReportingPeriod Period,
    DateOnly DueDate,
    ObligationStatus Status,
    DateTimeOffset? FirstSubmittedAt,
    bool IsLate,
    SubmissionStatus? SubmissionStatus,
    DateTimeOffset? SubmissionFirstSubmittedAt);

/// <summary>Findings of one rule and severity in one period.</summary>
/// <param name="Period">The reporting period.</param>
/// <param name="RuleCode">The rule code.</param>
/// <param name="Severity">The severity.</param>
/// <param name="Findings">How many findings (one per submitted revision that tripped the rule).</param>
/// <param name="Returns">How many returns they were on.</param>
public sealed record FindingCountRow(ReportingPeriod Period, string RuleCode, Severity Severity, int Findings, int Returns);

/// <summary>A numeric value of an approved return.</summary>
/// <param name="InstitutionId">The institution id.</param>
/// <param name="InstitutionCode">The institution code.</param>
/// <param name="InstitutionName">The institution name.</param>
/// <param name="Period">The reporting period.</param>
/// <param name="FieldCode">The field code.</param>
/// <param name="Value">The value.</param>
public sealed record ApprovedValueRow(
    Guid InstitutionId, string InstitutionCode, string InstitutionName, ReportingPeriod Period, string FieldCode, decimal Value);
