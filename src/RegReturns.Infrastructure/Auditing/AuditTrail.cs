using System.Data;

using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Auditing;
using RegReturns.Domain.Auditing;
using RegReturns.Infrastructure.Persistence;

namespace RegReturns.Infrastructure.Auditing;

/// <summary>
/// Appends audit entries to the chain in their own transaction. An exclusive application lock serialises
/// appends, so sequence numbers have no gaps and every entry links to the one before it (ADR 0016).
/// </summary>
/// <param name="contextOptions">Options for a dedicated context, so the caller's pending changes are never saved here.</param>
/// <param name="hasher">The keyed hasher.</param>
/// <param name="timeProvider">The clock.</param>
internal sealed class AuditTrail(
    DbContextOptions<RegReturnsDbContext> contextOptions,
    AuditHasher hasher,
    TimeProvider timeProvider) : IAuditTrail
{
    private const string LockSql = """
        DECLARE @result int;
        EXEC @result = sp_getapplock @Resource = N'RegReturns.AuditChain', @LockMode = N'Exclusive',
            @LockOwner = N'Transaction', @LockTimeout = 10000;
        IF @result < 0 THROW 51000, N'Could not lock the audit chain.', 1;
        """;

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
                await context.Database.ExecuteSqlRawAsync(LockSql, ct);

                var last = await context.AuditEntries
                    .OrderByDescending(e => e.Sequence)
                    .Select(e => new { e.Sequence, e.Hash })
                    .FirstOrDefaultAsync(ct);

                var entry = AuditEntry.Create(
                    at, rec.Action, rec.ActorType, rec.ActorSubjectId, rec.ActorDisplayName, rec.InstitutionCode,
                    rec.EntityType, rec.EntityId, rec.Details, rec.IpAddress, rec.CorrelationId);
                entry.Seal((last?.Sequence ?? 0) + 1, last?.Hash ?? AuditEntry.GenesisHash, hash.Compute);

                await context.AuditEntries.AddAsync(entry, ct);
                await context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return entry.Sequence;
            },
            verifySucceeded: null,
            cancellationToken);
    }
}
