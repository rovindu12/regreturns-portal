using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Identity;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Common;
using RegReturns.Domain.Identity;
using RegReturns.Domain.Periods;
using RegReturns.Domain.Submissions;

namespace RegReturns.Application.Supervision;

/// <summary>
/// Asks for the supervisors' worklist: returns waiting to be picked up, returns under review (which approvers
/// decide), returns sent back to their banks, and recent decisions. Drafts never appear: a bank's draft is its own.
/// </summary>
/// <param name="InstitutionId">Only this bank's returns, if set.</param>
/// <param name="ReturnTypeId">Only this return type, if set.</param>
/// <param name="LateOnly">Only returns first submitted after their due date.</param>
public sealed record GetSupervisionWorklist(Guid? InstitutionId = null, Guid? ReturnTypeId = null, bool LateOnly = false);

/// <summary>The supervisors' worklist.</summary>
/// <param name="ToPickUp">Submitted returns waiting for a reviewer, earliest due first.</param>
/// <param name="UnderReview">Returns being reviewed, which an approver can decide; longest waiting first.</param>
/// <param name="WithBanks">Returns sent back for correction, most recent first.</param>
/// <param name="RecentlyDecided">Approved and rejected returns of the last <see cref="SupervisionWorklistHandler.DecidedWithinDays"/> days, newest first.</param>
/// <param name="Institutions">The banks to filter by.</param>
/// <param name="ReturnTypes">The return types to filter by.</param>
public sealed record SupervisionWorklist(
    IReadOnlyList<WorklistRow> ToPickUp,
    IReadOnlyList<WorklistRow> UnderReview,
    IReadOnlyList<WorklistRow> WithBanks,
    IReadOnlyList<WorklistRow> RecentlyDecided,
    IReadOnlyList<WorklistFilterOption> Institutions,
    IReadOnlyList<WorklistFilterOption> ReturnTypes);

/// <summary>One return in the supervision worklist.</summary>
/// <param name="SubmissionId">The submission id.</param>
/// <param name="InstitutionCode">The bank's code.</param>
/// <param name="InstitutionName">The bank's name.</param>
/// <param name="ReturnTypeCode">The return type code.</param>
/// <param name="PeriodLabel">The period.</param>
/// <param name="DueDate">The due date.</param>
/// <param name="Status">The workflow status.</param>
/// <param name="Revision">The revision; above 1 means the bank has resubmitted it.</param>
/// <param name="IsLate">Whether it was first submitted after the due date.</param>
/// <param name="LastSubmittedAt">When the bank last submitted it.</param>
/// <param name="LastActivityAt">When its latest workflow step happened.</param>
/// <param name="ReviewerName">Who is reviewing it, if anyone.</param>
/// <param name="IsMine">Whether the caller is its reviewer.</param>
/// <param name="DeciderName">Who approved or rejected it.</param>
/// <param name="DecidedAt">When it was approved or rejected.</param>
public sealed record WorklistRow(
    Guid SubmissionId,
    string InstitutionCode,
    string InstitutionName,
    string ReturnTypeCode,
    string PeriodLabel,
    DateOnly DueDate,
    SubmissionStatus Status,
    int Revision,
    bool IsLate,
    DateTimeOffset? LastSubmittedAt,
    DateTimeOffset LastActivityAt,
    string? ReviewerName,
    bool IsMine,
    string? DeciderName,
    DateTimeOffset? DecidedAt);

/// <summary>A choice in a worklist filter.</summary>
/// <param name="Id">The id to filter by.</param>
/// <param name="Code">The code.</param>
/// <param name="Name">The name.</param>
public sealed record WorklistFilterOption(Guid Id, string Code, string Name);

/// <summary>Handles <see cref="GetSupervisionWorklist"/> for supervisor reviewers and approvers.</summary>
/// <param name="db">The unit of work.</param>
/// <param name="currentActor">The caller.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class SupervisionWorklistHandler(IAppDbContext db, ICurrentActor currentActor, TimeProvider timeProvider)
    : IQueryHandler<GetSupervisionWorklist, Result<SupervisionWorklist>>
{
    /// <summary>How far back the recent decisions go.</summary>
    public const int DecidedWithinDays = 30;

    /// <summary>The most recent decisions shown.</summary>
    public const int RecentDecisionLimit = 50;

    /// <inheritdoc />
    public async Task<Result<SupervisionWorklist>> HandleAsync(GetSupervisionWorklist query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var actorResult = await currentActor.GetAsync(cancellationToken);
        if (actorResult.IsFailure)
        {
            return actorResult.Error!;
        }

        var actor = actorResult.Value;
        if (!actor.HasRole(Role.SupervisorReviewer) && !actor.HasRole(Role.SupervisorApprover))
        {
            return SubmissionErrors.RoleRequired;
        }

        if (!actor.IsRegulatorStaff)
        {
            return SubmissionErrors.RegulatorOnly;
        }

        var decidedSince = timeProvider.GetUtcNow().AddDays(-DecidedWithinDays);
        var submissions = db.Submissions.AsNoTracking()
            .Where(s => s.FirstSubmittedAt != null)
            .Where(s => (s.Status != SubmissionStatus.Approved && s.Status != SubmissionStatus.Rejected) || s.DecidedAt >= decidedSince);
        if (query.InstitutionId is { } institutionId)
        {
            submissions = submissions.Where(s => s.InstitutionId == institutionId);
        }

        if (query.ReturnTypeId is { } returnTypeId)
        {
            submissions = submissions.Where(s => s.ReturnTypeId == returnTypeId);
        }

        if (query.LateOnly)
        {
            submissions = submissions.Where(s => s.IsLate);
        }

        var rows = await submissions
            .Join(db.Obligations, s => s.ObligationId, o => o.Id, (s, o) => new { Submission = s, o.Period, o.DueDate })
            .Join(db.ReturnTypes, x => x.Submission.ReturnTypeId, r => r.Id, (x, r) => new { x.Submission, x.Period, x.DueDate, ReturnTypeCode = r.Code })
            .Join(db.Institutions, x => x.Submission.InstitutionId, i => i.Id, (x, i) => new WorklistRowData(
                x.Submission.Id,
                i.Code,
                i.Name,
                x.ReturnTypeCode,
                x.Period,
                x.DueDate,
                x.Submission.Status,
                x.Submission.Revision,
                x.Submission.IsLate,
                x.Submission.LastSubmittedAt,
                x.Submission.Events.Max(e => e.OccurredAt),
                x.Submission.ReviewedByUserId,
                db.Users.Where(u => u.Id == x.Submission.ReviewedByUserId).Select(u => u.DisplayName).FirstOrDefault(),
                db.Users.Where(u => u.Id == x.Submission.DecidedByUserId).Select(u => u.DisplayName).FirstOrDefault(),
                x.Submission.DecidedAt))
            .ToListAsync(cancellationToken);

        var queue = rows.Select(r => r.ToRow(actor.UserId)).ToList();
        return new SupervisionWorklist(
            [.. queue.Where(r => r.Status == SubmissionStatus.Submitted).OrderBy(r => r.DueDate).ThenBy(r => r.LastSubmittedAt)],
            [.. queue.Where(r => r.Status == SubmissionStatus.UnderReview).OrderBy(r => r.LastSubmittedAt)],
            [.. queue.Where(r => r.Status == SubmissionStatus.ReturnedForCorrection).OrderByDescending(r => r.LastActivityAt)],
            [.. queue.Where(r => SubmissionWorkflow.IsFinal(r.Status)).OrderByDescending(r => r.DecidedAt).Take(RecentDecisionLimit)],
            await db.Institutions.AsNoTracking()
                .OrderBy(i => i.Code)
                .Select(i => new WorklistFilterOption(i.Id, i.Code, i.Name))
                .ToListAsync(cancellationToken),
            await db.ReturnTypes.AsNoTracking()
                .OrderBy(r => r.Code)
                .Select(r => new WorklistFilterOption(r.Id, r.Code, r.Name))
                .ToListAsync(cancellationToken));
    }

    private sealed record WorklistRowData(
        Guid SubmissionId,
        string InstitutionCode,
        string InstitutionName,
        string ReturnTypeCode,
        ReportingPeriod Period,
        DateOnly DueDate,
        SubmissionStatus Status,
        int Revision,
        bool IsLate,
        DateTimeOffset? LastSubmittedAt,
        DateTimeOffset LastActivityAt,
        Guid? ReviewerId,
        string? ReviewerName,
        string? DeciderName,
        DateTimeOffset? DecidedAt)
    {
        public WorklistRow ToRow(Guid callerId) => new(
            SubmissionId, InstitutionCode, InstitutionName, ReturnTypeCode, Period.Label, DueDate, Status, Revision, IsLate,
            LastSubmittedAt, LastActivityAt, ReviewerName, ReviewerId == callerId, DeciderName, DecidedAt);
    }
}
