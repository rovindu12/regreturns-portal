using RegReturns.Domain.Common;

namespace RegReturns.Domain.Submissions;

/// <summary>An immutable record of one workflow step, with the actor and comment.</summary>
public sealed class WorkflowEvent : Entity
{
    /// <summary>Maximum length of a comment.</summary>
    public const int CommentMaxLength = 2000;

    private WorkflowEvent()
    {
        ActorDisplayName = string.Empty;
    }

    /// <summary>Gets the owning submission id.</summary>
    public Guid SubmissionId { get; }

    /// <summary>Gets the revision the step applied to.</summary>
    public int Revision { get; private set; }

    /// <summary>Gets the action taken.</summary>
    public WorkflowAction Action { get; private set; }

    /// <summary>Gets the state before the step, or <see langword="null"/> on creation.</summary>
    public SubmissionStatus? FromStatus { get; private set; }

    /// <summary>Gets the state after the step.</summary>
    public SubmissionStatus ToStatus { get; private set; }

    /// <summary>Gets the internal id of the user who acted.</summary>
    public Guid ActorUserId { get; private set; }

    /// <summary>Gets the actor's display name at the time.</summary>
    public string ActorDisplayName { get; private set; }

    /// <summary>Gets the comment, if any.</summary>
    public string? Comment { get; private set; }

    /// <summary>Gets when the step happened.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    internal static WorkflowEvent Create(
        int revision,
        WorkflowAction action,
        SubmissionStatus? from,
        SubmissionStatus to,
        Identity.Actor actor,
        string? comment,
        DateTimeOffset at) => new()
        {
            Revision = revision,
            Action = action,
            FromStatus = from,
            ToStatus = to,
            ActorUserId = actor.UserId,
            ActorDisplayName = actor.DisplayName,
            Comment = comment,
            OccurredAt = at,
        };
}
