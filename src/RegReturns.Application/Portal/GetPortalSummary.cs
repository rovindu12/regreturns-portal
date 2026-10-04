using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Obligations;
using RegReturns.Domain.Submissions;

namespace RegReturns.Application.Portal;

/// <summary>Asks for the headline counts shown on the portal home page.</summary>
/// <param name="AsOf">The date used to decide what is overdue; defaults to today (UTC).</param>
public sealed record GetPortalSummary(DateOnly? AsOf = null);

/// <summary>Headline counts shown on the portal home page.</summary>
/// <param name="Institutions">Active institutions.</param>
/// <param name="ReturnTypes">Active return types.</param>
/// <param name="AwaitingReview">Submissions submitted or under review.</param>
/// <param name="Overdue">Obligations past their due date with nothing filed.</param>
/// <param name="ApprovedThisYear">Submissions approved in the current calendar year.</param>
public sealed record PortalSummary(int Institutions, int ReturnTypes, int AwaitingReview, int Overdue, int ApprovedThisYear);

/// <summary>Computes <see cref="PortalSummary"/>.</summary>
/// <param name="db">The unit of work.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class GetPortalSummaryHandler(IAppDbContext db, TimeProvider timeProvider)
    : IQueryHandler<GetPortalSummary, PortalSummary>
{
    /// <inheritdoc />
    public async Task<PortalSummary> HandleAsync(GetPortalSummary query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var now = timeProvider.GetUtcNow();
        var today = query.AsOf ?? DateOnly.FromDateTime(now.UtcDateTime);
        var startOfYear = new DateTimeOffset(now.Year, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var institutions = await db.Institutions.CountAsync(i => i.IsActive, cancellationToken);
        var returnTypes = await db.ReturnTypes.CountAsync(r => r.IsActive, cancellationToken);
        var awaitingReview = await db.Submissions.CountAsync(
            s => s.Status == SubmissionStatus.Submitted || s.Status == SubmissionStatus.UnderReview, cancellationToken);
        var overdue = await db.Obligations.CountAsync(
            o => o.Status == ObligationStatus.Open && o.DueDate < today, cancellationToken);
        var approved = await db.Submissions.CountAsync(
            s => s.Status == SubmissionStatus.Approved && s.DecidedAt >= startOfYear, cancellationToken);

        return new PortalSummary(institutions, returnTypes, awaitingReview, overdue, approved);
    }
}
