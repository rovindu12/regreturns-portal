using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Identity;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Common;
using RegReturns.Domain.Identity;
using RegReturns.Domain.Obligations;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;

namespace RegReturns.Application.Returns;

/// <summary>
/// Asks for a return as the portal shows it: fields by section, values, findings, uploads and workflow history. Bank
/// staff see their own bank's returns as an entry form; regulator staff see any return once it has been submitted
/// (<see cref="ReturnVisibility"/>).
/// </summary>
/// <param name="SubmissionId">The submission id.</param>
public sealed record GetReturnForm(Guid SubmissionId);

/// <summary>A return as the entry form shows it.</summary>
/// <param name="SubmissionId">The submission id.</param>
/// <param name="ObligationId">The obligation id.</param>
/// <param name="ReturnTypeCode">The return type code.</param>
/// <param name="ReturnTypeName">The return type name.</param>
/// <param name="PeriodLabel">The period.</param>
/// <param name="DueDate">The due date.</param>
/// <param name="TemplateVersion">The template version number the return is captured with.</param>
/// <param name="Status">The workflow status.</param>
/// <param name="Revision">The revision.</param>
/// <param name="Source">Where the data came from.</param>
/// <param name="EditVersion">The edit counter the form must send back when saving.</param>
/// <param name="IsValidated">Whether the latest values have been validated.</param>
/// <param name="CanEdit">Whether the caller may change the values (a maker, while the return is editable).</param>
/// <param name="CanJustify">Whether the caller may justify warnings (a maker or checker, while editable).</param>
/// <param name="Sections">The fields grouped by section, in display order.</param>
/// <param name="Findings">The current revision's findings, in field order.</param>
/// <param name="Uploads">Files uploaded to this return, newest first.</param>
/// <param name="Workflow">The bank, late flag, history and the steps the caller may take.</param>
public sealed record ReturnForm(
    Guid SubmissionId,
    Guid ObligationId,
    string ReturnTypeCode,
    string ReturnTypeName,
    string PeriodLabel,
    DateOnly DueDate,
    int TemplateVersion,
    SubmissionStatus Status,
    int Revision,
    SubmissionSource Source,
    int EditVersion,
    bool IsValidated,
    bool CanEdit,
    bool CanJustify,
    IReadOnlyList<ReturnFormSection> Sections,
    IReadOnlyList<ReturnFinding> Findings,
    IReadOnlyList<ReturnUpload> Uploads,
    ReturnWorkflowState Workflow)
{
    /// <summary>Gets the finding counts.</summary>
    public ValidationOutcome Outcome => new(
        Findings.Count(f => f.Severity == Severity.Error),
        Findings.Count(f => f.Severity == Severity.Warning),
        Findings.Count(f => f.Severity == Severity.Warning && f.Justification is null));
}

/// <summary>Where a return stands in its workflow, as the caller sees it.</summary>
/// <param name="InstitutionCode">The submitting bank's code.</param>
/// <param name="InstitutionName">The submitting bank's name.</param>
/// <param name="IsLate">Whether the return was first submitted after its due date.</param>
/// <param name="FirstSubmittedAt">When it was first submitted.</param>
/// <param name="LastSubmittedAt">When it was last submitted.</param>
/// <param name="LateIfSubmittedNow">Whether submitting now, for the first time, would mark the return late.</param>
/// <param name="ReviewerName">Who picked up the current revision for review, if anyone.</param>
/// <param name="Actions">The workflow steps the caller may take now.</param>
/// <param name="SubmitBlocker">Why the caller, a checker of the bank, may not submit it now, if that is the case.</param>
/// <param name="History">Every workflow step, newest first.</param>
public sealed record ReturnWorkflowState(
    string InstitutionCode,
    string InstitutionName,
    bool IsLate,
    DateTimeOffset? FirstSubmittedAt,
    DateTimeOffset? LastSubmittedAt,
    bool LateIfSubmittedNow,
    string? ReviewerName,
    IReadOnlyList<WorkflowAction> Actions,
    string? SubmitBlocker,
    IReadOnlyList<ReturnHistoryEntry> History)
{
    /// <summary>Gets the latest step, if it sent the return back or rejected it: what the bank must read first.</summary>
    public ReturnHistoryEntry? SupervisorVerdict => History.Count > 0
        && History[0].Action is WorkflowAction.ReturnForCorrection or WorkflowAction.Reject
            ? History[0]
            : null;

    /// <summary>Returns whether the caller may take a step.</summary>
    /// <param name="action">The step.</param>
    /// <returns><see langword="true"/> if the step is offered to the caller.</returns>
    public bool Allows(WorkflowAction action) => Actions.Contains(action);
}

/// <summary>One workflow step of a return.</summary>
/// <param name="Revision">The revision it applied to.</param>
/// <param name="Action">The step.</param>
/// <param name="FromStatus">The status before, or <see langword="null"/> when the draft was created.</param>
/// <param name="ToStatus">The status after.</param>
/// <param name="ActorName">Who took the step (their name at the time).</param>
/// <param name="Comment">Their comment, if any.</param>
/// <param name="OccurredAt">When.</param>
public sealed record ReturnHistoryEntry(
    int Revision,
    WorkflowAction Action,
    SubmissionStatus? FromStatus,
    SubmissionStatus ToStatus,
    string ActorName,
    string? Comment,
    DateTimeOffset OccurredAt);

/// <summary>A section of the form.</summary>
/// <param name="Name">The section heading.</param>
/// <param name="Fields">The fields, in display order.</param>
public sealed record ReturnFormSection(string Name, IReadOnlyList<ReturnFormField> Fields);

/// <summary>A field of the form with its value and findings.</summary>
/// <param name="Code">The field code.</param>
/// <param name="Label">The label.</param>
/// <param name="DataType">The data type.</param>
/// <param name="Unit">The unit.</param>
/// <param name="Precision">Maximum decimal places for numbers.</param>
/// <param name="Value">The value as entered, if any.</param>
/// <param name="Findings">The current findings on this field.</param>
public sealed record ReturnFormField(
    string Code, string Label, FieldDataType DataType, string Unit, int Precision, string? Value, IReadOnlyList<ReturnFinding> Findings);

/// <summary>A validation finding on the current revision.</summary>
/// <param name="Id">The finding id (used to justify a warning).</param>
/// <param name="RuleCode">The rule code.</param>
/// <param name="FieldCode">The field it is reported against.</param>
/// <param name="FieldLabel">The field's label.</param>
/// <param name="Severity">Error or warning.</param>
/// <param name="Message">The message.</param>
/// <param name="Justification">The justification for a warning, if given.</param>
/// <param name="JustifiedBy">Who justified it.</param>
/// <param name="JustifiedAt">When it was justified.</param>
public sealed record ReturnFinding(
    Guid Id,
    string RuleCode,
    string FieldCode,
    string FieldLabel,
    Severity Severity,
    string Message,
    string? Justification,
    string? JustifiedBy,
    DateTimeOffset? JustifiedAt);

/// <summary>A file uploaded to the return.</summary>
/// <param name="FileName">The sanitised file name.</param>
/// <param name="ContentType">The content type from the signature check.</param>
/// <param name="SizeBytes">The size in bytes.</param>
/// <param name="Revision">The revision it was uploaded for.</param>
/// <param name="UploadedBy">Who uploaded it.</param>
/// <param name="UploadedAt">When.</param>
public sealed record ReturnUpload(string FileName, string ContentType, int SizeBytes, int Revision, string UploadedBy, DateTimeOffset UploadedAt);

/// <summary>Handles <see cref="GetReturnForm"/>.</summary>
/// <param name="db">The unit of work.</param>
/// <param name="currentActor">The caller.</param>
/// <param name="timeProvider">The clock, to tell whether submitting now would be late.</param>
public sealed class GetReturnFormHandler(IAppDbContext db, ICurrentActor currentActor, TimeProvider timeProvider)
    : IQueryHandler<GetReturnForm, Result<ReturnForm>>
{
    /// <inheritdoc />
    public async Task<Result<ReturnForm>> HandleAsync(GetReturnForm query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var actorResult = await currentActor.GetAsync(cancellationToken);
        if (actorResult.IsFailure)
        {
            return actorResult.Error!;
        }

        var actor = actorResult.Value;
        var submission = await db.Submissions.AsNoTracking()
            .VisibleTo(actor)
            .Include(s => s.Values)
            .Include(s => s.Findings)
            .Include(s => s.Events)
            .SingleOrDefaultAsync(s => s.Id == query.SubmissionId, cancellationToken);
        if (submission is null)
        {
            return SubmissionErrors.NotFound;
        }

        var template = await BankReturnAccess.LoadTemplateAsync(db, submission.TemplateVersionId, cancellationToken);
        var context = await db.Obligations.AsNoTracking()
            .Where(o => o.Id == submission.ObligationId)
            .Join(db.ReturnTypes, o => o.ReturnTypeId, r => r.Id, (o, r) => new { o.Period, o.DueDate, o.InstitutionId, r.Code, r.Name })
            .Join(db.Institutions, x => x.InstitutionId, i => i.Id, (x, i) => new
            {
                x.Period,
                x.DueDate,
                x.Code,
                x.Name,
                InstitutionCode = i.Code,
                InstitutionName = i.Name,
            })
            .SingleAsync(cancellationToken);
        var uploads = await db.StoredFiles.AsNoTracking()
            .Where(f => f.SubmissionId == submission.Id)
            .OrderByDescending(f => f.UploadedAt)
            .Join(db.Users, f => f.UploadedByUserId, u => u.Id, (f, u) => new ReturnUpload(
                f.FileName, f.ContentType, f.SizeBytes, f.Revision, u.DisplayName, f.UploadedAt))
            .ToListAsync(cancellationToken);

        var findings = await FindingsAsync(submission, template, cancellationToken);
        var byField = findings.ToLookup(f => f.FieldCode, StringComparer.Ordinal);
        var sections = template.Fields
            .OrderBy(f => f.DisplayOrder)
            .GroupBy(f => f.Section, StringComparer.Ordinal)
            .Select(g => new ReturnFormSection(g.Key, [.. g.Select(f => new ReturnFormField(
                f.Code, f.Label, f.DataType, f.Unit, f.Precision, submission.FindValue(f.Code)?.RawValue, [.. byField[f.Code]]))]))
            .ToList();

        var isBankStaff = actor.HasRole(Role.BankMaker) || actor.HasRole(Role.BankChecker);
        return new ReturnForm(
            submission.Id, submission.ObligationId, context.Code, context.Name, context.Period.Label, context.DueDate,
            template.Version, submission.Status, submission.Revision, submission.Source, submission.EditVersion,
            submission.ValidatedEditVersion == submission.EditVersion,
            submission.IsEditable && actor.HasRole(Role.BankMaker),
            submission.IsEditable && isBankStaff,
            sections, findings, uploads,
            Workflow(submission, actor, context.InstitutionCode, context.InstitutionName, context.DueDate));
    }

    private ReturnWorkflowState Workflow(
        Submission submission, Actor actor, string institutionCode, string institutionName, DateOnly dueDate)
    {
        var history = submission.Events
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Select(e => new ReturnHistoryEntry(e.Revision, e.Action, e.FromStatus, e.ToStatus, e.ActorDisplayName, e.Comment, e.OccurredAt))
            .ToList();
        var reviewer = history.Find(e => e.Action == WorkflowAction.StartReview && e.Revision == submission.Revision);
        var permit = actor.HasRole(Role.BankChecker) && submission.IsEditable
            ? submission.Permits(WorkflowAction.Submit, actor)
            : Result.Success();

        return new ReturnWorkflowState(
            institutionCode,
            institutionName,
            submission.IsLate,
            submission.FirstSubmittedAt,
            submission.LastSubmittedAt,
            submission.FirstSubmittedAt is null && ReturnObligation.IsLateArrival(dueDate, timeProvider.GetUtcNow()),
            submission.ReviewedByUserId is null ? null : reviewer?.ActorName,
            submission.ActionsFor(actor),
            permit.Error?.Message,
            history);
    }

    private async Task<List<ReturnFinding>> FindingsAsync(Submission submission, TemplateVersion template, CancellationToken cancellationToken)
    {
        var current = submission.CurrentFindings.ToList();
        var justifierIds = current.Where(f => f.JustifiedByUserId is not null).Select(f => f.JustifiedByUserId!.Value).Distinct().ToList();
        var justifiers = await db.Users.AsNoTracking()
            .Where(u => justifierIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var order = template.Fields.ToDictionary(f => f.Code, f => (f.DisplayOrder, f.Label), StringComparer.Ordinal);
        var rules = template.Rules.ToDictionary(r => r.Id, r => r.RuleType);

        return [.. current
            .OrderBy(f => order[f.FieldCode].DisplayOrder)
            .ThenBy(f => rules.GetValueOrDefault(f.RuleId))
            .ThenBy(f => f.RuleCode, StringComparer.Ordinal)
            .Select(f => new ReturnFinding(
                f.Id, f.RuleCode, f.FieldCode, order[f.FieldCode].Label, f.Severity, f.Message, f.Justification,
                f.JustifiedByUserId is { } by ? justifiers.GetValueOrDefault(by) : null, f.JustifiedAt))];
    }
}
