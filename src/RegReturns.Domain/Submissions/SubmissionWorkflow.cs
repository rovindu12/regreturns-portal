using System.Collections.Frozen;

namespace RegReturns.Domain.Submissions;

/// <summary>
/// The submission state machine: which actions are allowed from which state, and where they lead.
/// Role and segregation-of-duties checks live on <see cref="Submission"/>.
/// </summary>
public static class SubmissionWorkflow
{
    private static readonly FrozenDictionary<(SubmissionStatus From, WorkflowAction Action), SubmissionStatus> Transitions =
        new Dictionary<(SubmissionStatus, WorkflowAction), SubmissionStatus>
        {
            [(SubmissionStatus.Draft, WorkflowAction.Submit)] = SubmissionStatus.Submitted,
            [(SubmissionStatus.ReturnedForCorrection, WorkflowAction.Submit)] = SubmissionStatus.Submitted,
            [(SubmissionStatus.Submitted, WorkflowAction.StartReview)] = SubmissionStatus.UnderReview,
            [(SubmissionStatus.UnderReview, WorkflowAction.ReturnForCorrection)] = SubmissionStatus.ReturnedForCorrection,
            [(SubmissionStatus.UnderReview, WorkflowAction.Approve)] = SubmissionStatus.Approved,
            [(SubmissionStatus.UnderReview, WorkflowAction.Reject)] = SubmissionStatus.Rejected,
        }.ToFrozenDictionary();

    /// <summary>Returns the state an action leads to, or <see langword="null"/> if the action is not allowed.</summary>
    /// <param name="from">The current state.</param>
    /// <param name="action">The action.</param>
    /// <returns>The target state, or <see langword="null"/>.</returns>
    public static SubmissionStatus? TargetOf(SubmissionStatus from, WorkflowAction action) =>
        Transitions.TryGetValue((from, action), out var to) ? to : null;

    /// <summary>Returns the actions allowed from a state, ignoring who is asking.</summary>
    /// <param name="from">The current state.</param>
    /// <returns>The allowed actions.</returns>
    public static IReadOnlyList<WorkflowAction> ActionsFrom(SubmissionStatus from) =>
        Transitions.Keys.Where(k => k.From == from).Select(k => k.Action).Order().ToList();

    /// <summary>Returns whether a state lets the bank edit values.</summary>
    /// <param name="status">The state.</param>
    /// <returns><see langword="true"/> for <see cref="SubmissionStatus.Draft"/> and <see cref="SubmissionStatus.ReturnedForCorrection"/>.</returns>
    public static bool IsEditable(SubmissionStatus status) =>
        status is SubmissionStatus.Draft or SubmissionStatus.ReturnedForCorrection;

    /// <summary>Returns whether a state is final.</summary>
    /// <param name="status">The state.</param>
    /// <returns><see langword="true"/> for approved or rejected submissions.</returns>
    public static bool IsFinal(SubmissionStatus status) =>
        status is SubmissionStatus.Approved or SubmissionStatus.Rejected;
}
