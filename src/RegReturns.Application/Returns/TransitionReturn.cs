using System.Diagnostics;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Diagnostics;
using RegReturns.Application.Identity;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Common;
using RegReturns.Domain.Submissions;

namespace RegReturns.Application.Returns;

/// <summary>
/// Moves a return one step through its workflow: a bank checker submits it, a supervisor reviewer picks it up, a
/// reviewer or approver returns it for correction, and an approver approves or rejects it. The state, role and
/// segregation-of-duties rules live on <see cref="Submission"/>; the host checks its policy (TOTP for approvers) first.
/// </summary>
/// <param name="SubmissionId">The submission id.</param>
/// <param name="Action">The step to take.</param>
/// <param name="Comment">The comment, required for every step except starting a review.</param>
public sealed record TransitionReturn(Guid SubmissionId, WorkflowAction Action, string? Comment = null);

/// <summary>Where a return stands after a workflow step.</summary>
/// <param name="Status">The new status.</param>
/// <param name="Revision">The revision (it goes up when a return is sent back for correction).</param>
/// <param name="IsLate">Whether the return was first submitted after its due date.</param>
public sealed record TransitionOutcome(SubmissionStatus Status, int Revision, bool IsLate);

/// <summary>Handles <see cref="TransitionReturn"/>.</summary>
/// <param name="db">The unit of work; the save also appends the change to the audit trail.</param>
/// <param name="currentActor">The caller.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed class TransitionReturnHandler(
    IAppDbContext db, ICurrentActor currentActor, TimeProvider timeProvider, ILogger<TransitionReturnHandler> logger)
    : ICommandHandler<TransitionReturn, Result<TransitionOutcome>>
{
    /// <summary>Outcome tag of a step that was taken.</summary>
    internal const string Done = "done";

    /// <summary>Outcome tag of a step that was refused.</summary>
    internal const string Refused = "refused";

    /// <inheritdoc />
    public async Task<Result<TransitionOutcome>> HandleAsync(TransitionReturn command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var activity = RegReturnsTelemetry.ActivitySource.StartActivity("returns.transition");
        activity?.SetTag(RegReturnsTelemetry.SubmissionIdTag, command.SubmissionId);
        activity?.SetTag(RegReturnsTelemetry.WorkflowActionTag, command.Action.ToString());

        var actor = await currentActor.GetAsync(cancellationToken);
        if (actor.IsFailure)
        {
            return Refuse(command, actor.Error!, activity);
        }

        var submission = await db.Submissions.VisibleTo(actor.Value)
            .Include(s => s.Findings)
            .SingleOrDefaultAsync(s => s.Id == command.SubmissionId, cancellationToken);
        if (submission is null)
        {
            return Refuse(command, SubmissionErrors.NotFound, activity);
        }

        var obligation = await db.Obligations.SingleAsync(o => o.Id == submission.ObligationId, cancellationToken);
        var from = submission.Status;
        var wasSubmitted = submission.FirstSubmittedAt is not null;
        var now = timeProvider.GetUtcNow();
        var comment = command.Comment ?? string.Empty;
        var moved = command.Action switch
        {
            WorkflowAction.Submit => submission.Submit(actor.Value, obligation, comment, now),
            WorkflowAction.StartReview => submission.StartReview(actor.Value, now),
            WorkflowAction.ReturnForCorrection => submission.ReturnForCorrection(actor.Value, comment, now),
            WorkflowAction.Approve => submission.Approve(actor.Value, obligation, comment, now),
            WorkflowAction.Reject => submission.Reject(actor.Value, obligation, comment, now),
            _ => SubmissionErrors.InvalidTransition,
        };
        if (moved.IsFailure)
        {
            return Refuse(command, moved.Error!, activity);
        }

        var saved = await db.SaveOrConflictAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Refuse(command, saved.Error!, activity);
        }

        ReturnsLog.Transitioned(logger, submission.Id, command.Action, from, submission.Status, submission.Revision, submission.IsLate);
        activity?.SetTag(RegReturnsTelemetry.OutcomeTag, Done);
        RegReturnsTelemetry.WorkflowTransitions.Add(
            1,
            new(RegReturnsTelemetry.WorkflowActionTag, command.Action.ToString()),
            new(RegReturnsTelemetry.OutcomeTag, Done));
        if (!wasSubmitted && submission.IsLate)
        {
            RegReturnsTelemetry.LateSubmissions.Add(1);
        }

        return new TransitionOutcome(submission.Status, submission.Revision, submission.IsLate);
    }

    private Error Refuse(TransitionReturn command, Error error, Activity? activity)
    {
        ReturnsLog.TransitionRefused(logger, command.SubmissionId, command.Action, error.Code);
        activity?.SetTag(RegReturnsTelemetry.OutcomeTag, Refused);
        activity?.SetTag(RegReturnsTelemetry.ErrorCodeTag, error.Code);
        RegReturnsTelemetry.WorkflowTransitions.Add(
            1,
            new(RegReturnsTelemetry.WorkflowActionTag, command.Action.ToString()),
            new(RegReturnsTelemetry.OutcomeTag, Refused),
            new(RegReturnsTelemetry.ErrorCodeTag, error.Code));
        return error;
    }
}
