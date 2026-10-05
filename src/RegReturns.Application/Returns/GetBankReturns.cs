using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Identity;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Common;
using RegReturns.Domain.Obligations;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;

namespace RegReturns.Application.Returns;

/// <summary>Asks for the caller's bank's filing obligations and where each return stands.</summary>
/// <param name="Months">How many months of due dates to look back.</param>
public sealed record GetBankReturns(int Months = 13);

/// <summary>The caller's bank and its obligations, most urgent first.</summary>
/// <param name="InstitutionCode">The bank's code.</param>
/// <param name="InstitutionName">The bank's name.</param>
/// <param name="Rows">The obligations: open ones by due date, then filed ones newest first.</param>
public sealed record BankReturnsOverview(string InstitutionCode, string InstitutionName, IReadOnlyList<BankReturnRow> Rows);

/// <summary>One obligation and its latest return.</summary>
/// <param name="ObligationId">The obligation id.</param>
/// <param name="ReturnTypeCode">The return type code.</param>
/// <param name="ReturnTypeName">The return type name.</param>
/// <param name="PeriodLabel">The period, such as <c>2026-03</c>.</param>
/// <param name="DueDate">The due date.</param>
/// <param name="ObligationStatus">Open, in progress or fulfilled.</param>
/// <param name="IsOverdue">Whether nothing has been filed and the due date has passed.</param>
/// <param name="SubmissionId">The latest return, if one was started.</param>
/// <param name="SubmissionStatus">Its status.</param>
/// <param name="Revision">Its revision.</param>
/// <param name="Errors">Errors on its current revision.</param>
/// <param name="UnjustifiedWarnings">Warnings without a justification on its current revision.</param>
/// <param name="IsLate">Whether it was first submitted after the due date.</param>
public sealed record BankReturnRow(
    Guid ObligationId,
    string ReturnTypeCode,
    string ReturnTypeName,
    string PeriodLabel,
    DateOnly DueDate,
    ObligationStatus ObligationStatus,
    bool IsOverdue,
    Guid? SubmissionId,
    SubmissionStatus? SubmissionStatus,
    int? Revision,
    int Errors,
    int UnjustifiedWarnings,
    bool IsLate)
{
    /// <summary>Gets a value indicating whether the bank still has work to do on it.</summary>
    public bool IsToDo => ObligationStatus == ObligationStatus.Open || SubmissionStatus == Domain.Submissions.SubmissionStatus.ReturnedForCorrection;
}

/// <summary>Handles <see cref="GetBankReturns"/>.</summary>
/// <param name="db">The unit of work.</param>
/// <param name="currentActor">The caller.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class GetBankReturnsHandler(IAppDbContext db, ICurrentActor currentActor, TimeProvider timeProvider)
    : IQueryHandler<GetBankReturns, Result<BankReturnsOverview>>
{
    /// <inheritdoc />
    public async Task<Result<BankReturnsOverview>> HandleAsync(GetBankReturns query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var actor = await BankReturnAccess.BankActorAsync(currentActor, cancellationToken);
        if (actor.IsFailure)
        {
            return actor.Error!;
        }

        var institutionId = actor.Value.InstitutionId!.Value;
        var institution = await db.Institutions.AsNoTracking()
            .Where(i => i.Id == institutionId)
            .Select(i => new { i.Code, i.Name })
            .SingleAsync(cancellationToken);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var from = today.AddMonths(-Math.Clamp(query.Months, 1, 60));
        var obligations = await db.Obligations.AsNoTracking()
            .Where(o => o.InstitutionId == institutionId && o.DueDate >= from)
            .Join(db.ReturnTypes, o => o.ReturnTypeId, r => r.Id, (o, r) => new { Obligation = o, r.Code, r.Name })
            .ToListAsync(cancellationToken);
        var ids = obligations.Select(o => o.Obligation.Id).ToList();
        var submissions = await db.Submissions.AsNoTracking()
            .Where(s => ids.Contains(s.ObligationId))
            .Select(s => new
            {
                s.ObligationId,
                s.Id,
                s.Status,
                s.Revision,
                s.IsLate,
                s.CreatedAt,
                Errors = s.Findings.Count(f => f.Revision == s.Revision && f.Severity == Severity.Error),
                Unjustified = s.Findings.Count(f => f.Revision == s.Revision && f.Severity == Severity.Warning && f.Justification == null),
            })
            .ToListAsync(cancellationToken);
        var latest = submissions
            .GroupBy(s => s.ObligationId)
            .ToDictionary(g => g.Key, g => g.MaxBy(s => s.CreatedAt)!);

        var rows = obligations
            .Select(o =>
            {
                var s = latest.GetValueOrDefault(o.Obligation.Id);
                return new BankReturnRow(
                    o.Obligation.Id, o.Code, o.Name, o.Obligation.Period.Label, o.Obligation.DueDate, o.Obligation.Status,
                    o.Obligation.IsOverdue(today), s?.Id, s?.Status, s?.Revision, s?.Errors ?? 0, s?.Unjustified ?? 0, s?.IsLate ?? false);
            })
            .OrderByDescending(r => r.IsToDo)
            .ThenBy(r => r.IsToDo ? r.DueDate.DayNumber : -r.DueDate.DayNumber)
            .ThenBy(r => r.ReturnTypeCode, StringComparer.Ordinal)
            .ToList();
        return new BankReturnsOverview(institution.Code, institution.Name, rows);
    }
}
