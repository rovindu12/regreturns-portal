using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Identity;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Common;
using RegReturns.Domain.Identity;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;

namespace RegReturns.Application.Returns;

/// <summary>Asks for a return of the caller's bank as an entry form: fields by section, values and findings.</summary>
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
    IReadOnlyList<ReturnUpload> Uploads)
{
    /// <summary>Gets the finding counts.</summary>
    public ValidationOutcome Outcome => new(
        Findings.Count(f => f.Severity == Severity.Error),
        Findings.Count(f => f.Severity == Severity.Warning),
        Findings.Count(f => f.Severity == Severity.Warning && f.Justification is null));
}

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
public sealed class GetReturnFormHandler(IAppDbContext db, ICurrentActor currentActor)
    : IQueryHandler<GetReturnForm, Result<ReturnForm>>
{
    /// <inheritdoc />
    public async Task<Result<ReturnForm>> HandleAsync(GetReturnForm query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var actorResult = await BankReturnAccess.BankActorAsync(currentActor, cancellationToken);
        if (actorResult.IsFailure)
        {
            return actorResult.Error!;
        }

        var actor = actorResult.Value;
        var submission = await db.Submissions.AsNoTracking()
            .Include(s => s.Values)
            .Include(s => s.Findings)
            .SingleOrDefaultAsync(s => s.Id == query.SubmissionId && s.InstitutionId == actor.InstitutionId, cancellationToken);
        if (submission is null)
        {
            return SubmissionErrors.NotFound;
        }

        var template = await BankReturnAccess.LoadTemplateAsync(db, submission.TemplateVersionId, cancellationToken);
        var context = await db.Obligations.AsNoTracking()
            .Where(o => o.Id == submission.ObligationId)
            .Join(db.ReturnTypes, o => o.ReturnTypeId, r => r.Id, (o, r) => new { o.Period, o.DueDate, r.Code, r.Name })
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
            sections, findings, uploads);
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
