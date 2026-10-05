using System.Data;

using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Auditing;
using RegReturns.Domain.Auditing;
using RegReturns.Infrastructure.Persistence;

namespace RegReturns.Infrastructure.Auditing;

/// <summary>
/// Appends audit entries to the chain in their own transaction. The exclusive chain lock (<see cref="AuditChain"/>),
/// shared with saves that audit their data changes, serialises appends, so sequence numbers have no gaps and every
/// entry links to the one before it (ADR 0016).
/// </summary>
/// <param name="contextOptions">Options for a dedicated context, so the caller's pending changes are never saved here.</param>
/// <param name="hasher">The keyed hasher.</param>
/// <param name="timeProvider">The clock.</param>
internal sealed class AuditTrail(
    DbContextOptions<RegReturnsDbContext> contextOptions,
    AuditHasher hasher,
    TimeProvider timeProvider) : IAuditTrail
{
    /// <inheritdoc />
    public async Task<long> RecordAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        var occurredAt = timeProvider.GetUtcNow();

        await using var db = new RegReturnsDbContext(contextOptions);
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            (db, record, occurredAt, hasher),
            static async (_, state, ct) =>
            {
                var (context, rec, at, hash) = state;
                context.ChangeTracker.Clear();
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
                var head = await AuditChain.LockAsync(context, ct);

                var entry = AuditEntry.Create(
                    at, rec.Action, rec.ActorType, rec.ActorSubjectId, rec.ActorDisplayName, rec.InstitutionCode,
                    rec.EntityType, rec.EntityId, rec.Details, rec.IpAddress, rec.CorrelationId);
                entry.Seal(head.Sequence + 1, head.Hash, hash.Compute);

                await context.AuditEntries.AddAsync(entry, ct);
                await context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return entry.Sequence;
            },
            verifySucceeded: null,
            cancellationToken);
    }
}
