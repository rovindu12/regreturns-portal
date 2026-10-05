using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Auditing;

namespace RegReturns.Application.Auditing;

/// <summary>Asks for one page of the audit trail, newest first, optionally filtered.</summary>
/// <param name="Page">The 1-based page number; out-of-range pages are clamped.</param>
/// <param name="Action">Only entries recording this action.</param>
/// <param name="EntityType">Only entries about this entity type, such as <c>Submission</c>.</param>
/// <param name="EntityId">Only entries about the entity with this id.</param>
/// <param name="Actor">Only entries by this actor: their exact subject id, or part of their display name.</param>
public sealed record GetAuditTrail(
    int Page = 1, AuditAction? Action = null, string? EntityType = null, string? EntityId = null, string? Actor = null)
{
    /// <summary>The number of entries on a page.</summary>
    public const int PageSize = 25;
}

/// <summary>One audit entry, ready to display.</summary>
/// <param name="Sequence">The position in the chain.</param>
/// <param name="OccurredAt">When it happened (UTC).</param>
/// <param name="ActorType">The kind of actor.</param>
/// <param name="ActorSubjectId">The actor's subject id, client id or <c>system</c>.</param>
/// <param name="ActorDisplayName">The actor's display name at the time, if known.</param>
/// <param name="InstitutionCode">The actor's institution, if any.</param>
/// <param name="Action">What happened.</param>
/// <param name="EntityType">The affected entity type, if any.</param>
/// <param name="EntityId">The affected entity id, if any.</param>
/// <param name="Details">Extra context.</param>
/// <param name="IpAddress">The caller's IP address.</param>
/// <param name="CorrelationId">The W3C trace id, which joins the entry to the logs.</param>
/// <param name="Changes">The stored change document, if the entry records a data change.</param>
/// <param name="ChangeItems">The change document read for display, or <see langword="null"/> if there is none or it cannot be read.</param>
public sealed record AuditTrailRow(
    long Sequence,
    DateTimeOffset OccurredAt,
    ActorType ActorType,
    string ActorSubjectId,
    string? ActorDisplayName,
    string? InstitutionCode,
    AuditAction Action,
    string? EntityType,
    string? EntityId,
    string? Details,
    string? IpAddress,
    string? CorrelationId,
    string? Changes,
    IReadOnlyList<AuditChangeItem>? ChangeItems);

/// <summary>A page of the audit trail.</summary>
/// <param name="Rows">The entries, newest first.</param>
/// <param name="Page">The page shown (1-based).</param>
/// <param name="TotalCount">The number of entries matching the filters.</param>
public sealed record AuditTrailPage(IReadOnlyList<AuditTrailRow> Rows, int Page, int TotalCount)
{
    /// <summary>Gets the number of pages (at least 1).</summary>
    public int PageCount => Math.Max(1, (TotalCount + GetAuditTrail.PageSize - 1) / GetAuditTrail.PageSize);
}

/// <summary>Handles <see cref="GetAuditTrail"/>.</summary>
/// <param name="db">The unit of work.</param>
public sealed class GetAuditTrailHandler(IAppDbContext db) : IQueryHandler<GetAuditTrail, AuditTrailPage>
{
    /// <inheritdoc />
    public async Task<AuditTrailPage> HandleAsync(GetAuditTrail query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var entries = db.AuditEntries;
        if (query.Action is { } action)
        {
            entries = entries.Where(e => e.Action == action);
        }

        if (Clean(query.EntityType) is { } entityType)
        {
            entries = entries.Where(e => e.EntityType == entityType);
        }

        if (Clean(query.EntityId) is { } entityId)
        {
            entries = entries.Where(e => e.EntityId == entityId);
        }

        if (Clean(query.Actor) is { } actor)
        {
            entries = entries.Where(e => e.ActorSubjectId == actor || (e.ActorDisplayName != null && e.ActorDisplayName.Contains(actor)));
        }

        var total = await entries.CountAsync(cancellationToken);
        var pageCount = Math.Max(1, (total + GetAuditTrail.PageSize - 1) / GetAuditTrail.PageSize);
        var page = Math.Clamp(query.Page, 1, pageCount);
        var rows = await entries
            .OrderByDescending(e => e.Sequence)
            .Skip((page - 1) * GetAuditTrail.PageSize)
            .Take(GetAuditTrail.PageSize)
            .Select(e => new AuditTrailRow(
                e.Sequence, e.OccurredAt, e.ActorType, e.ActorSubjectId, e.ActorDisplayName, e.InstitutionCode, e.Action,
                e.EntityType, e.EntityId, e.Details, e.IpAddress, e.CorrelationId, e.Changes, null))
            .ToListAsync(cancellationToken);

        return new AuditTrailPage([.. rows.Select(r => r with { ChangeItems = AuditChanges.Parse(r.Changes) })], page, total);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
