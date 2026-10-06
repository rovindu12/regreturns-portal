using RegReturns.Domain.Submissions;

namespace RegReturns.Application.Reporting;

/// <summary>The wording of states on report pages and in exported files, so both say the same.</summary>
public static class ReportLabels
{
    /// <summary>Gets the label of a compliance state.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The label.</returns>
    public static string Of(ComplianceState state) => state switch
    {
        ComplianceState.NoObligation => "No return due",
        ComplianceState.NotDue => "Not due yet",
        ComplianceState.OnTime => "On time",
        ComplianceState.Late => "Late",
        ComplianceState.Overdue => "Overdue",
        _ => state.ToString(),
    };

    /// <summary>Gets the label of a workflow status.</summary>
    /// <param name="status">The status.</param>
    /// <returns>The label.</returns>
    public static string Of(SubmissionStatus status) => status switch
    {
        SubmissionStatus.Draft => "Draft",
        SubmissionStatus.Submitted => "Submitted",
        SubmissionStatus.UnderReview => "Under review",
        SubmissionStatus.ReturnedForCorrection => "Returned for correction",
        SubmissionStatus.Approved => "Approved",
        SubmissionStatus.Rejected => "Rejected",
        _ => status.ToString(),
    };

    /// <summary>Gets the short label of a compliance state, for grid cells.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The short label.</returns>
    public static string ShortOf(ComplianceState state) => state switch
    {
        ComplianceState.NoObligation => "-",
        ComplianceState.NotDue => "Not due",
        _ => Of(state),
    };
}
