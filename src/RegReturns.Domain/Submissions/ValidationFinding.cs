using RegReturns.Domain.Common;
using RegReturns.Domain.Templates;

namespace RegReturns.Domain.Submissions;

/// <summary>A failed validation rule for one revision of a submission.</summary>
public sealed class ValidationFinding : Entity
{
    /// <summary>Minimum length of a warning justification.</summary>
    public const int JustificationMinLength = 20;

    /// <summary>Maximum length of a warning justification.</summary>
    public const int JustificationMaxLength = 2000;

    private ValidationFinding()
    {
        RuleCode = string.Empty;
        FieldCode = string.Empty;
        Message = string.Empty;
    }

    /// <summary>Gets the owning submission id.</summary>
    public Guid SubmissionId { get; }

    /// <summary>Gets the submission revision the finding belongs to.</summary>
    public int Revision { get; private set; }

    /// <summary>Gets the rule that failed.</summary>
    public Guid RuleId { get; private set; }

    /// <summary>Gets the code of the rule that failed.</summary>
    public string RuleCode { get; private set; }

    /// <summary>Gets the field the finding is reported against.</summary>
    public string FieldCode { get; private set; }

    /// <summary>Gets whether the finding blocks submission.</summary>
    public Severity Severity { get; private set; }

    /// <summary>Gets the message shown to the user.</summary>
    public string Message { get; private set; }

    /// <summary>Gets the written justification for a warning, if given.</summary>
    public string? Justification { get; private set; }

    /// <summary>Gets who justified the warning.</summary>
    public Guid? JustifiedByUserId { get; private set; }

    /// <summary>Gets when the warning was justified.</summary>
    public DateTimeOffset? JustifiedAt { get; private set; }

    /// <summary>Gets a value indicating whether the finding still prevents submission.</summary>
    public bool BlocksSubmission => Severity == Severity.Error || string.IsNullOrWhiteSpace(Justification);

    internal static ValidationFinding Create(int revision, FindingDraft draft) => new()
    {
        Revision = revision,
        RuleId = draft.RuleId,
        RuleCode = draft.RuleCode,
        FieldCode = draft.FieldCode,
        Severity = draft.Severity,
        Message = draft.Message,
    };

    internal bool Matches(FindingDraft draft) =>
        RuleId == draft.RuleId && string.Equals(FieldCode, draft.FieldCode, StringComparison.Ordinal);

    internal void Justify(string justification, Guid userId, DateTimeOffset at)
    {
        Justification = justification;
        JustifiedByUserId = userId;
        JustifiedAt = at;
    }

    internal void CopyJustificationFrom(ValidationFinding previous)
    {
        Justification = previous.Justification;
        JustifiedByUserId = previous.JustifiedByUserId;
        JustifiedAt = previous.JustifiedAt;
    }
}

/// <summary>A validation failure produced by the validation engine, before it is attached to a submission.</summary>
/// <param name="RuleId">The rule id.</param>
/// <param name="RuleCode">The rule code.</param>
/// <param name="FieldCode">The field the finding is reported against.</param>
/// <param name="Severity">The severity.</param>
/// <param name="Message">The message.</param>
public sealed record FindingDraft(Guid RuleId, string RuleCode, string FieldCode, Severity Severity, string Message);
