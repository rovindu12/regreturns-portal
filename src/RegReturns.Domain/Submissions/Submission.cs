using RegReturns.Domain.Common;
using RegReturns.Domain.Identity;
using RegReturns.Domain.Obligations;
using RegReturns.Domain.Templates;

namespace RegReturns.Domain.Submissions;

/// <summary>
/// A bank's return for one obligation, moving through the maker-checker and supervisory review workflow.
/// All role, institution and segregation-of-duties rules are enforced here so every entry point
/// (web, upload, API, migration) behaves the same way.
/// </summary>
public sealed class Submission : Entity
{
    private readonly List<SubmissionValue> _values = [];
    private readonly List<ValidationFinding> _findings = [];
    private readonly List<WorkflowEvent> _events = [];

    private Submission()
    {
    }

    /// <summary>Gets the obligation this submission fulfils.</summary>
    public Guid ObligationId { get; private set; }

    /// <summary>Gets the submitting bank.</summary>
    public Guid InstitutionId { get; private set; }

    /// <summary>Gets the return type.</summary>
    public Guid ReturnTypeId { get; private set; }

    /// <summary>Gets the template version the values were captured with.</summary>
    public Guid TemplateVersionId { get; private set; }

    /// <summary>Gets the workflow state.</summary>
    public SubmissionStatus Status { get; private set; }

    /// <summary>Gets the revision, starting at 1 and increasing each time the return is sent back for correction.</summary>
    public int Revision { get; private set; }

    /// <summary>Gets where the data came from.</summary>
    public SubmissionSource Source { get; private set; }

    /// <summary>Gets the maker who created the draft.</summary>
    public Guid PreparedByUserId { get; private set; }

    /// <summary>Gets the user who last changed the values.</summary>
    public Guid LastEditedByUserId { get; private set; }

    /// <summary>Gets the checker who last submitted the return.</summary>
    public Guid? SubmittedByUserId { get; private set; }

    /// <summary>Gets the reviewer of the current revision.</summary>
    public Guid? ReviewedByUserId { get; private set; }

    /// <summary>Gets the approver who approved or rejected the return.</summary>
    public Guid? DecidedByUserId { get; private set; }

    /// <summary>Gets when the draft was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets when the values last changed.</summary>
    public DateTimeOffset LastEditedAt { get; private set; }

    /// <summary>Gets when the return was first submitted.</summary>
    public DateTimeOffset? FirstSubmittedAt { get; private set; }

    /// <summary>Gets when the return was last submitted.</summary>
    public DateTimeOffset? LastSubmittedAt { get; private set; }

    /// <summary>Gets when the return was approved or rejected.</summary>
    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>Gets a value indicating whether the first submission arrived after the due date.</summary>
    public bool IsLate { get; private set; }

    /// <summary>Gets a counter that increases on every change to the values.</summary>
    public int EditVersion { get; private set; }

    /// <summary>Gets the <see cref="EditVersion"/> that was last validated, or <see langword="null"/> if not validated.</summary>
    public int? ValidatedEditVersion { get; private set; }

    /// <summary>Gets the values.</summary>
    public IReadOnlyCollection<SubmissionValue> Values => _values.AsReadOnly();

    /// <summary>Gets the validation findings for every revision.</summary>
    public IReadOnlyCollection<ValidationFinding> Findings => _findings.AsReadOnly();

    /// <summary>Gets the workflow history in the order it was recorded.</summary>
    public IReadOnlyCollection<WorkflowEvent> Events => _events.AsReadOnly();

    /// <summary>Gets the findings for the current revision.</summary>
    public IEnumerable<ValidationFinding> CurrentFindings => _findings.Where(f => f.Revision == Revision);

    /// <summary>Gets a value indicating whether the bank can still change the values.</summary>
    public bool IsEditable => SubmissionWorkflow.IsEditable(Status);

    /// <summary>Creates a draft for an obligation.</summary>
    /// <param name="obligation">The obligation to fulfil.</param>
    /// <param name="template">The published template version to capture values with.</param>
    /// <param name="maker">The bank maker creating the draft.</param>
    /// <param name="source">Where the data comes from.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The draft, or the rule that was broken.</returns>
    public static Result<Submission> CreateDraft(
        ReturnObligation obligation,
        TemplateVersion template,
        Actor maker,
        SubmissionSource source,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(obligation);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(maker);

        if (template.ReturnTypeId != obligation.ReturnTypeId)
        {
            return SubmissionErrors.TemplateMismatch;
        }

        var bankCheck = RequireBankRole(maker, Role.BankMaker, obligation.InstitutionId);
        if (bankCheck.IsFailure)
        {
            return bankCheck.Error!;
        }

        var submission = new Submission
        {
            ObligationId = obligation.Id,
            InstitutionId = obligation.InstitutionId,
            ReturnTypeId = obligation.ReturnTypeId,
            TemplateVersionId = template.Id,
            Status = SubmissionStatus.Draft,
            Revision = 1,
            Source = source,
            PreparedByUserId = maker.UserId,
            LastEditedByUserId = maker.UserId,
            CreatedAt = now,
            LastEditedAt = now,
        };
        submission._events.Add(WorkflowEvent.Create(1, WorkflowAction.Create, null, SubmissionStatus.Draft, maker, null, now));
        return submission;
    }

    /// <summary>Returns the value of a field, if present.</summary>
    /// <param name="fieldCode">The field code.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public SubmissionValue? FindValue(string fieldCode) =>
        _values.Find(v => string.Equals(v.FieldCode, fieldCode, StringComparison.Ordinal));

    /// <summary>Sets field values. Blank values are kept as blanks so required-field rules can report them.</summary>
    /// <param name="template">The submission's template version.</param>
    /// <param name="values">Values keyed by field code.</param>
    /// <param name="editor">The bank maker making the change.</param>
    /// <param name="now">The current time.</param>
    /// <returns>Success, or the rule that was broken.</returns>
    public Result SetValues(
        TemplateVersion template, IReadOnlyDictionary<string, string?> values, Actor editor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(editor);

        if (template.Id != TemplateVersionId)
        {
            return SubmissionErrors.TemplateMismatch;
        }

        if (!IsEditable)
        {
            return SubmissionErrors.NotEditable;
        }

        var bankCheck = RequireBankRole(editor, Role.BankMaker, InstitutionId);
        if (bankCheck.IsFailure)
        {
            return bankCheck;
        }

        var fields = new List<(TemplateField Field, string? Raw)>(values.Count);
        foreach (var (code, raw) in values)
        {
            var field = template.FindField(code);
            if (field is null)
            {
                return SubmissionErrors.UnknownField.WithMessage($"Field '{code}' is not in the return template.");
            }

            fields.Add((field, raw));
        }

        foreach (var (field, raw) in fields)
        {
            var existing = FindValue(field.Code);
            if (existing is null)
            {
                _values.Add(SubmissionValue.Create(field, raw));
            }
            else
            {
                existing.Update(field, raw);
            }
        }

        EditVersion++;
        LastEditedByUserId = editor.UserId;
        LastEditedAt = now;
        return Result.Success();
    }

    /// <summary>
    /// Replaces the findings for the current revision with a fresh validation run.
    /// Justifications carry over for warnings that are still present.
    /// </summary>
    /// <param name="findings">The findings produced by the validation engine.</param>
    /// <returns>Success, or failure if the submission is no longer editable.</returns>
    public Result RecordValidation(IEnumerable<FindingDraft> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        if (!IsEditable)
        {
            return SubmissionErrors.NotEditable;
        }

        var previous = CurrentFindings.ToList();
        _findings.RemoveAll(f => f.Revision == Revision);

        foreach (var draft in findings)
        {
            var finding = ValidationFinding.Create(Revision, draft);
            var carried = previous.Find(p => p.Severity == Severity.Warning && p.Matches(draft));
            if (draft.Severity == Severity.Warning && carried?.Justification is not null)
            {
                finding.CopyJustificationFrom(carried);
            }

            _findings.Add(finding);
        }

        ValidatedEditVersion = EditVersion;
        return Result.Success();
    }

    /// <summary>Records a written justification for a warning on the current revision.</summary>
    /// <param name="findingId">The warning finding id.</param>
    /// <param name="justification">The justification text.</param>
    /// <param name="actor">A maker or checker of the submitting bank.</param>
    /// <param name="now">The current time.</param>
    /// <returns>Success, or the rule that was broken.</returns>
    public Result JustifyWarning(Guid findingId, string justification, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!IsEditable)
        {
            return SubmissionErrors.NotEditable;
        }

        if (!actor.BelongsTo(InstitutionId))
        {
            return SubmissionErrors.WrongInstitution;
        }

        if (!actor.HasRole(Role.BankMaker) && !actor.HasRole(Role.BankChecker))
        {
            return SubmissionErrors.RoleRequired;
        }

        var text = justification?.Trim() ?? string.Empty;
        if (text.Length is < ValidationFinding.JustificationMinLength or > ValidationFinding.JustificationMaxLength)
        {
            return SubmissionErrors.JustificationLength;
        }

        var finding = CurrentFindings.FirstOrDefault(f => f.Id == findingId && f.Severity == Severity.Warning);
        if (finding is null)
        {
            return SubmissionErrors.FindingNotFound;
        }

        finding.Justify(text, actor.UserId, now);
        return Result.Success();
    }

    /// <summary>
    /// The bank checker submits (or resubmits) the return to the regulator.
    /// The checker cannot be the maker, values must be validated, errors fixed and warnings justified.
    /// </summary>
    /// <param name="checker">The bank checker.</param>
    /// <param name="obligation">The obligation, updated with the first submission time and late flag.</param>
    /// <param name="comment">The checker's comment.</param>
    /// <param name="now">The current time.</param>
    /// <returns>Success, or the rule that was broken.</returns>
    public Result Submit(Actor checker, ReturnObligation obligation, string comment, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(obligation);

        var checks = Check(
            () => obligation.Id == ObligationId ? Result.Success() : SubmissionErrors.TemplateMismatch,
            () => RequireTransition(WorkflowAction.Submit),
            () => RequireBankRole(checker, Role.BankChecker, InstitutionId),
            () => checker.UserId == PreparedByUserId || checker.UserId == LastEditedByUserId
                ? SubmissionErrors.CheckerIsMaker
                : Result.Success(),
            () => RequireComment(comment),
            () => EditVersion == 0 ? SubmissionErrors.NoValues : Result.Success(),
            () => ValidatedEditVersion != EditVersion ? SubmissionErrors.ValidationOutdated : Result.Success(),
            () => CurrentFindings.Any(f => f.Severity == Severity.Error) ? SubmissionErrors.HasErrors : Result.Success(),
            () => CurrentFindings.Any(f => f.BlocksSubmission) ? SubmissionErrors.UnjustifiedWarnings : Result.Success());
        if (checks.IsFailure)
        {
            return checks;
        }

        if (FirstSubmittedAt is null)
        {
            FirstSubmittedAt = now;
            IsLate = obligation.WouldBeLate(now);
        }

        LastSubmittedAt = now;
        SubmittedByUserId = checker.UserId;
        obligation.MarkSubmitted(now);
        Move(WorkflowAction.Submit, checker, comment, now);
        return Result.Success();
    }

    /// <summary>A supervisor reviewer picks up a submitted return.</summary>
    /// <param name="reviewer">The supervisor reviewer.</param>
    /// <param name="now">The current time.</param>
    /// <returns>Success, or the rule that was broken.</returns>
    public Result StartReview(Actor reviewer, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(reviewer);
        var checks = Check(
            () => RequireTransition(WorkflowAction.StartReview),
            () => RequireRegulatorRole(reviewer, Role.SupervisorReviewer));
        if (checks.IsFailure)
        {
            return checks;
        }

        ReviewedByUserId = reviewer.UserId;
        Move(WorkflowAction.StartReview, reviewer, null, now);
        return Result.Success();
    }

    /// <summary>A supervisor reviewer or approver sends the return back to the bank, starting a new revision.</summary>
    /// <param name="supervisor">The supervisor.</param>
    /// <param name="comment">What the bank must correct.</param>
    /// <param name="now">The current time.</param>
    /// <returns>Success, or the rule that was broken.</returns>
    public Result ReturnForCorrection(Actor supervisor, string comment, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(supervisor);
        var role = supervisor.HasRole(Role.SupervisorApprover) ? Role.SupervisorApprover : Role.SupervisorReviewer;
        var checks = Check(
            () => RequireTransition(WorkflowAction.ReturnForCorrection),
            () => RequireRegulatorRole(supervisor, role),
            () => RequireComment(comment));
        if (checks.IsFailure)
        {
            return checks;
        }

        Move(WorkflowAction.ReturnForCorrection, supervisor, comment, now);
        Revision++;
        ReviewedByUserId = null;
        ValidatedEditVersion = null;
        return Result.Success();
    }

    /// <summary>A supervisor approver approves the return. The approver cannot be its reviewer.</summary>
    /// <param name="approver">The supervisor approver.</param>
    /// <param name="obligation">The obligation, marked fulfilled.</param>
    /// <param name="comment">The approver's comment.</param>
    /// <param name="now">The current time.</param>
    /// <returns>Success, or the rule that was broken.</returns>
    public Result Approve(Actor approver, ReturnObligation obligation, string comment, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(obligation);
        var decided = Decide(WorkflowAction.Approve, approver, comment, now);
        if (decided.IsSuccess)
        {
            obligation.MarkFulfilled();
        }

        return decided;
    }

    /// <summary>A supervisor approver rejects the return. The approver cannot be its reviewer.</summary>
    /// <param name="approver">The supervisor approver.</param>
    /// <param name="obligation">The obligation, re-opened so the bank can file again.</param>
    /// <param name="comment">The reason for rejection.</param>
    /// <param name="now">The current time.</param>
    /// <returns>Success, or the rule that was broken.</returns>
    public Result Reject(Actor approver, ReturnObligation obligation, string comment, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(obligation);
        var decided = Decide(WorkflowAction.Reject, approver, comment, now);
        if (decided.IsSuccess)
        {
            obligation.Reopen();
        }

        return decided;
    }

    private static Result Check(params Func<Result>[] rules)
    {
        foreach (var rule in rules)
        {
            var result = rule();
            if (result.IsFailure)
            {
                return result;
            }
        }

        return Result.Success();
    }

    private static Result RequireBankRole(Actor actor, Role role, Guid institutionId)
    {
        if (!actor.HasRole(role))
        {
            return SubmissionErrors.RoleRequired;
        }

        return actor.BelongsTo(institutionId) ? Result.Success() : SubmissionErrors.WrongInstitution;
    }

    private static Result RequireRegulatorRole(Actor actor, Role role)
    {
        if (!actor.HasRole(role))
        {
            return SubmissionErrors.RoleRequired;
        }

        return actor.IsRegulatorStaff ? Result.Success() : SubmissionErrors.RegulatorOnly;
    }

    private static Result RequireComment(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return SubmissionErrors.CommentRequired;
        }

        return comment.Trim().Length > WorkflowEvent.CommentMaxLength
            ? SubmissionErrors.CommentRequired.WithMessage(
                $"A comment must be at most {WorkflowEvent.CommentMaxLength} characters.")
            : Result.Success();
    }

    private Result Decide(WorkflowAction action, Actor approver, string comment, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(approver);
        var checks = Check(
            () => RequireTransition(action),
            () => RequireRegulatorRole(approver, Role.SupervisorApprover),
            () => approver.UserId == ReviewedByUserId ? SubmissionErrors.ApproverIsReviewer : Result.Success(),
            () => RequireComment(comment));
        if (checks.IsFailure)
        {
            return checks;
        }

        DecidedByUserId = approver.UserId;
        DecidedAt = now;
        Move(action, approver, comment, now);
        return Result.Success();
    }

    private Result RequireTransition(WorkflowAction action) =>
        SubmissionWorkflow.TargetOf(Status, action) is null ? SubmissionErrors.InvalidTransition : Result.Success();

    private void Move(WorkflowAction action, Actor actor, string? comment, DateTimeOffset now)
    {
        var from = Status;
        Status = SubmissionWorkflow.TargetOf(from, action)
            ?? throw new InvalidOperationException($"Transition {from} -> {action} was not checked before moving.");
        _events.Add(WorkflowEvent.Create(Revision, action, from, Status, actor, comment?.Trim(), now));
    }
}
