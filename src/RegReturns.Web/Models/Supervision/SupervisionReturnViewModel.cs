using RegReturns.Application.Returns;
using RegReturns.Domain.Submissions;

namespace RegReturns.Web.Models.Supervision;

/// <summary>The supervision page of one return, plus what the supervisor posted when a step was refused.</summary>
/// <param name="Form">The return.</param>
/// <param name="CanDecide">Whether the caller passes the approval policy (approver role and, when enforced, a TOTP sign-in).</param>
public sealed record SupervisionReturnViewModel(ReturnForm Form, bool CanDecide)
{
    /// <summary>Gets the step that was refused.</summary>
    public WorkflowAction? FailedAction { get; init; }

    /// <summary>Gets the comment the supervisor posted with it.</summary>
    public string? Comment { get; init; }

    /// <summary>Gets why the step was refused.</summary>
    public string? Error { get; init; }

    /// <summary>Gets a value indicating whether the caller may approve or reject the return now.</summary>
    public bool ShowsDecision => CanDecide && (Form.Workflow.Allows(WorkflowAction.Approve) || Form.Workflow.Allows(WorkflowAction.Reject));

    /// <summary>Gets a value indicating whether the decision form is shown at all.</summary>
    public bool ShowsDecisionForm => ShowsDecision || Form.Workflow.Allows(WorkflowAction.ReturnForCorrection);

    /// <summary>Gets a value indicating whether an approver must sign in again with TOTP before deciding.</summary>
    public bool NeedsStepUp => !CanDecide && (Form.Workflow.Allows(WorkflowAction.Approve) || Form.Workflow.Allows(WorkflowAction.Reject));
}
