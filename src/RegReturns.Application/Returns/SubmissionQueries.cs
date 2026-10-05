using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Identity;
using RegReturns.Application.Messaging;
using RegReturns.Application.Paging;
using RegReturns.Domain.Common;
using RegReturns.Domain.Periods;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;

namespace RegReturns.Application.Returns;

/// <summary>
/// Asks for a page of the returns the caller may see (<see cref="ReturnVisibility"/>), newest first. A bank system
/// sees its own bank's returns, drafts included.
/// </summary>
/// <param name="Paging">The page to return.</param>
/// <param name="Status">Only returns in this status, if set.</param>
/// <param name="ReturnTypeCode">Only returns of this type, if set (case-insensitive).</param>
/// <param name="Period">Only returns for this reporting period, if set.</param>
public sealed record GetSubmissions(
    PageRequest Paging, SubmissionStatus? Status = null, string? ReturnTypeCode = null, ReportingPeriod? Period = null);

/// <summary>A return as a list shows it.</summary>
/// <param name="Id">The submission id.</param>
/// <param name="ReturnTypeCode">The return type code.</param>
/// <param name="PeriodLabel">The reporting period, such as <c>2027-03</c>.</param>
/// <param name="DueDate">The due date.</param>
/// <param name="TemplateVersion">The template version number it is captured with.</param>
/// <param name="Status">The workflow status.</param>
/// <param name="Revision">The revision (1, then one more each time it is returned for correction).</param>
/// <param name="Source">Where its values came from.</param>
/// <param name="IsValidated">Whether the latest values have been validated.</param>
/// <param name="Errors">Errors on the current revision.</param>
/// <param name="Warnings">Warnings on the current revision.</param>
/// <param name="UnjustifiedWarnings">Warnings still waiting for a justification.</param>
/// <param name="IsLate">Whether it was first submitted after the due date.</param>
/// <param name="CreatedAt">When it was started.</param>
/// <param name="LastEditedAt">When its values last changed.</param>
/// <param name="FirstSubmittedAt">When it was first submitted to the regulator.</param>
/// <param name="DecidedAt">When it was approved or rejected.</param>
public sealed record SubmissionSummary(
    Guid Id,
    string ReturnTypeCode,
    string PeriodLabel,
    DateOnly DueDate,
    int TemplateVersion,
    SubmissionStatus Status,
    int Revision,
    SubmissionSource Source,
    bool IsValidated,
    int Errors,
    int Warnings,
    int UnjustifiedWarnings,
    bool IsLate,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastEditedAt,
    DateTimeOffset? FirstSubmittedAt,
    DateTimeOffset? DecidedAt);

/// <summary>Handles <see cref="GetSubmissions"/>.</summary>
/// <param name="db">The unit of work.</param>
/// <param name="currentActor">The caller.</param>
public sealed class GetSubmissionsHandler(IAppDbContext db, ICurrentActor currentActor)
    : IQueryHandler<GetSubmissions, Result<PagedList<SubmissionSummary>>>
{
    /// <inheritdoc />
    public async Task<Result<PagedList<SubmissionSummary>>> HandleAsync(GetSubmissions query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var actor = await currentActor.GetAsync(cancellationToken);
        if (actor.IsFailure)
        {
            return actor.Error!;
        }

        var submissions = db.Submissions.AsNoTracking().VisibleTo(actor.Value);
        if (query.Status is { } status)
        {
            submissions = submissions.Where(s => s.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.ReturnTypeCode))
        {
            var code = query.ReturnTypeCode.Trim().ToUpperInvariant();
            submissions = submissions.Where(s => db.ReturnTypes.Any(r => r.Id == s.ReturnTypeId && r.Code == code));
        }

        if (query.Period is { } period)
        {
            submissions = submissions.Where(s => db.Obligations.Any(o => o.Id == s.ObligationId
                && o.Period.Frequency == period.Frequency
                && o.Period.Year == period.Year
                && o.Period.Number == period.Number));
        }

        var total = await submissions.CountAsync(cancellationToken);
        var ids = await submissions
            .OrderByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.Id)
            .Skip(query.Paging.Skip)
            .Take(query.Paging.Take)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);
        var rows = await SubmissionSummaries.Project(db, db.Submissions.AsNoTracking().Where(s => ids.Contains(s.Id)))
            .ToDictionaryAsync(r => r.Id, cancellationToken);
        return new PagedList<SubmissionSummary>(
            [.. ids.Select(id => SubmissionSummaries.ToSummary(rows[id]))], Math.Max(query.Paging.Page, 1), query.Paging.Take, total);
    }
}

/// <summary>Asks for one return the caller may see, with its values and workflow history.</summary>
/// <param name="SubmissionId">The submission id.</param>
public sealed record GetSubmission(Guid SubmissionId);

/// <summary>A return with its values and history.</summary>
/// <param name="Summary">Where the return stands.</param>
/// <param name="EditVersion">The edit counter, which goes up with every change of values.</param>
/// <param name="Values">A value (or <see langword="null"/>) for every field of its template, in display order.</param>
/// <param name="History">The workflow steps, oldest first. Who took each step is left out: bank systems need what
/// happened, and names are personal data.</param>
public sealed record SubmissionDetail(
    SubmissionSummary Summary, int EditVersion, IReadOnlyList<SubmissionFieldValue> Values, IReadOnlyList<SubmissionStep> History);

/// <summary>The value of one field.</summary>
/// <param name="FieldCode">The field code.</param>
/// <param name="Value">The value as entered, or <see langword="null"/> when blank.</param>
public sealed record SubmissionFieldValue(string FieldCode, string? Value);

/// <summary>One workflow step of a return.</summary>
/// <param name="Revision">The revision it applied to.</param>
/// <param name="Action">The step.</param>
/// <param name="FromStatus">The status before, or <see langword="null"/> when the draft was created.</param>
/// <param name="ToStatus">The status after.</param>
/// <param name="Comment">The comment, for example why the regulator sent it back.</param>
/// <param name="OccurredAt">When.</param>
public sealed record SubmissionStep(
    int Revision, WorkflowAction Action, SubmissionStatus? FromStatus, SubmissionStatus ToStatus, string? Comment, DateTimeOffset OccurredAt);

/// <summary>Handles <see cref="GetSubmission"/>.</summary>
/// <param name="db">The unit of work.</param>
/// <param name="currentActor">The caller.</param>
public sealed class GetSubmissionHandler(IAppDbContext db, ICurrentActor currentActor)
    : IQueryHandler<GetSubmission, Result<SubmissionDetail>>
{
    /// <inheritdoc />
    public async Task<Result<SubmissionDetail>> HandleAsync(GetSubmission query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var actor = await currentActor.GetAsync(cancellationToken);
        if (actor.IsFailure)
        {
            return actor.Error!;
        }

        var visible = db.Submissions.AsNoTracking().VisibleTo(actor.Value).Where(s => s.Id == query.SubmissionId);
        var row = await SubmissionSummaries.Project(db, visible).SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return SubmissionErrors.NotFound;
        }

        var submission = await db.Submissions.AsNoTracking()
            .Include(s => s.Values)
            .Include(s => s.Events)
            .SingleAsync(s => s.Id == query.SubmissionId, cancellationToken);
        var fields = await db.TemplateVersions.AsNoTracking()
            .Where(v => v.Id == submission.TemplateVersionId)
            .SelectMany(v => v.Fields)
            .OrderBy(f => f.DisplayOrder)
            .Select(f => f.Code)
            .ToListAsync(cancellationToken);

        return new SubmissionDetail(
            SubmissionSummaries.ToSummary(row),
            submission.EditVersion,
            [.. fields.Select(code => new SubmissionFieldValue(code, submission.FindValue(code)?.RawValue))],
            [.. submission.Events
                .OrderBy(e => e.OccurredAt)
                .ThenBy(e => e.Id)
                .Select(e => new SubmissionStep(e.Revision, e.Action, e.FromStatus, e.ToStatus, e.Comment, e.OccurredAt))]);
    }
}

/// <summary>Asks for the validation findings of a return's current revision.</summary>
/// <param name="SubmissionId">The submission id.</param>
public sealed record GetSubmissionValidation(Guid SubmissionId);

/// <summary>The findings of a return's current revision.</summary>
/// <param name="SubmissionId">The submission id.</param>
/// <param name="Status">The return's status.</param>
/// <param name="Revision">The revision the findings belong to.</param>
/// <param name="IsValidated">Whether the latest values have been validated; findings of older values are outdated.</param>
/// <param name="Findings">The findings, in field order, then by rule type and code.</param>
public sealed record SubmissionValidation(
    Guid SubmissionId, SubmissionStatus Status, int Revision, bool IsValidated, IReadOnlyList<SubmissionFinding> Findings)
{
    /// <summary>Gets the number of errors, which block submission.</summary>
    public int Errors => Findings.Count(f => f.Severity == Severity.Error);

    /// <summary>Gets the number of warnings.</summary>
    public int Warnings => Findings.Count(f => f.Severity == Severity.Warning);

    /// <summary>Gets the number of warnings without a justification, which also block submission.</summary>
    public int UnjustifiedWarnings => Findings.Count(f => f.Severity == Severity.Warning && f.Justification is null);

    /// <summary>
    /// Gets a value indicating whether a checker could submit the return as it stands: it is still with the bank, its
    /// latest values are validated, and nothing blocks it.
    /// </summary>
    public bool IsReadyToSubmit => SubmissionWorkflow.IsEditable(Status) && IsValidated && Errors == 0 && UnjustifiedWarnings == 0;

    /// <summary>Builds the validation view of a submission.</summary>
    /// <param name="submission">The submission, with findings loaded.</param>
    /// <param name="template">Its template version, with fields and rules.</param>
    /// <returns>The current revision's findings, ordered as the validation engine reports them.</returns>
    public static SubmissionValidation Of(Submission submission, TemplateVersion template)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(template);
        var order = template.Fields.ToDictionary(f => f.Code, f => f.DisplayOrder, StringComparer.Ordinal);
        var rules = template.Rules.ToDictionary(r => r.Id, r => r.RuleType);
        return new SubmissionValidation(
            submission.Id,
            submission.Status,
            submission.Revision,
            submission.ValidatedEditVersion == submission.EditVersion,
            [.. submission.CurrentFindings
                .OrderBy(f => order.GetValueOrDefault(f.FieldCode, int.MaxValue))
                .ThenBy(f => rules.GetValueOrDefault(f.RuleId))
                .ThenBy(f => f.RuleCode, StringComparer.Ordinal)
                .Select(f => new SubmissionFinding(f.Id, f.RuleCode, f.FieldCode, f.Severity, f.Message, f.Justification, f.JustifiedAt))]);
    }
}

/// <summary>A validation finding.</summary>
/// <param name="Id">The finding id.</param>
/// <param name="RuleCode">The rule that raised it.</param>
/// <param name="FieldCode">The field it is reported against.</param>
/// <param name="Severity">Error or warning.</param>
/// <param name="Message">The message.</param>
/// <param name="Justification">The bank's justification of a warning, if given.</param>
/// <param name="JustifiedAt">When the warning was justified.</param>
public sealed record SubmissionFinding(
    Guid Id, string RuleCode, string FieldCode, Severity Severity, string Message, string? Justification, DateTimeOffset? JustifiedAt);

/// <summary>Handles <see cref="GetSubmissionValidation"/>.</summary>
/// <param name="db">The unit of work.</param>
/// <param name="currentActor">The caller.</param>
public sealed class GetSubmissionValidationHandler(IAppDbContext db, ICurrentActor currentActor)
    : IQueryHandler<GetSubmissionValidation, Result<SubmissionValidation>>
{
    /// <inheritdoc />
    public async Task<Result<SubmissionValidation>> HandleAsync(GetSubmissionValidation query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var actor = await currentActor.GetAsync(cancellationToken);
        if (actor.IsFailure)
        {
            return actor.Error!;
        }

        var submission = await db.Submissions.AsNoTracking()
            .VisibleTo(actor.Value)
            .Include(s => s.Findings)
            .SingleOrDefaultAsync(s => s.Id == query.SubmissionId, cancellationToken);
        if (submission is null)
        {
            return SubmissionErrors.NotFound;
        }

        var template = await BankReturnAccess.LoadTemplateAsync(db, submission.TemplateVersionId, cancellationToken);
        return SubmissionValidation.Of(submission, template);
    }
}

/// <summary>Projects submissions to <see cref="SubmissionSummary"/> rows in one query.</summary>
internal static class SubmissionSummaries
{
    /// <summary>Adds the obligation, return type, template version and finding counts to each submission.</summary>
    public static IQueryable<SummaryRow> Project(IAppDbContext db, IQueryable<Submission> submissions) =>
        from s in submissions
        join o in db.Obligations on s.ObligationId equals o.Id
        join r in db.ReturnTypes on s.ReturnTypeId equals r.Id
        join v in db.TemplateVersions on s.TemplateVersionId equals v.Id
        select new SummaryRow(
            s.Id,
            r.Code,
            o.Period,
            o.DueDate,
            v.Version,
            s.Status,
            s.Revision,
            s.Source,
            s.ValidatedEditVersion == s.EditVersion,
            s.Findings.Count(f => f.Revision == s.Revision && f.Severity == Severity.Error),
            s.Findings.Count(f => f.Revision == s.Revision && f.Severity == Severity.Warning),
            s.Findings.Count(f => f.Revision == s.Revision && f.Severity == Severity.Warning && f.Justification == null),
            s.IsLate,
            s.CreatedAt,
            s.LastEditedAt,
            s.FirstSubmittedAt,
            s.DecidedAt);

    /// <summary>Turns a projected row into the summary (the period label is computed in memory).</summary>
    public static SubmissionSummary ToSummary(SummaryRow row) => new(
        row.Id, row.ReturnTypeCode, row.Period.Label, row.DueDate, row.TemplateVersion, row.Status, row.Revision, row.Source,
        row.IsValidated, row.Errors, row.Warnings, row.UnjustifiedWarnings, row.IsLate, row.CreatedAt, row.LastEditedAt,
        row.FirstSubmittedAt, row.DecidedAt);

    /// <summary>A projected submission.</summary>
    internal sealed record SummaryRow(
        Guid Id,
        string ReturnTypeCode,
        ReportingPeriod Period,
        DateOnly DueDate,
        int TemplateVersion,
        SubmissionStatus Status,
        int Revision,
        SubmissionSource Source,
        bool IsValidated,
        int Errors,
        int Warnings,
        int UnjustifiedWarnings,
        bool IsLate,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastEditedAt,
        DateTimeOffset? FirstSubmittedAt,
        DateTimeOffset? DecidedAt);
}
