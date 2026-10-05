using Microsoft.EntityFrameworkCore;

using RegReturns.Domain.Auditing;
using RegReturns.Infrastructure.Persistence;

namespace RegReturns.Infrastructure.Auditing;

/// <summary>The newest entry of the chain: where the next entry links on.</summary>
/// <param name="Sequence">Its sequence number, or 0 for an empty chain.</param>
/// <param name="Hash">Its hash, or <see cref="AuditEntry.GenesisHash"/> for an empty chain.</param>
internal readonly record struct AuditChainHead(long Sequence, string Hash)
{
    /// <summary>Gets the head of an empty chain.</summary>
    public static AuditChainHead Empty { get; } = new(0, AuditEntry.GenesisHash);
}

/// <summary>
/// The one lock every writer of the chain takes before it reads the head and appends (ADR 0016): <see cref="AuditTrail"/>
/// for events, and saves that audit their data changes (ADR 0024). Held until the caller's transaction ends, so
/// sequence numbers have no gaps and every entry links to the one before it.
/// </summary>
internal static class AuditChain
{
    /// <summary>The <c>sp_getapplock</c> resource name shared by every writer.</summary>
    public const string LockResource = "RegReturns.AuditChain";

    // A writer waits up to 10 seconds for the lock, then gives up with error 51000.
    private const string LockSql = $"""
        DECLARE @result int;
        EXEC @result = sp_getapplock @Resource = N'{LockResource}', @LockMode = N'Exclusive',
            @LockOwner = N'Transaction', @LockTimeout = 10000;
        IF @result < 0 THROW 51000, N'Could not lock the audit chain.', 1;
        """;

    /// <summary>Takes the chain lock in the current transaction and reads the head.</summary>
    /// <param name="context">A context with an open transaction.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The head to append after.</returns>
    public static async Task<AuditChainHead> LockAsync(RegReturnsDbContext context, CancellationToken cancellationToken)
    {
        await context.Database.ExecuteSqlRawAsync(LockSql, cancellationToken);
        return await ReadHeadAsync(context, cancellationToken);
    }

    /// <summary>Takes the chain lock in the current transaction and reads the head.</summary>
    /// <param name="context">A context with an open transaction.</param>
    /// <returns>The head to append after.</returns>
    public static AuditChainHead Lock(RegReturnsDbContext context)
    {
        context.Database.ExecuteSqlRaw(LockSql);
        var last = context.AuditEntries.AsNoTracking()
            .OrderByDescending(e => e.Sequence)
            .Select(e => new { e.Sequence, e.Hash })
            .FirstOrDefault();
        return last is null ? AuditChainHead.Empty : new AuditChainHead(last.Sequence, last.Hash);
    }

    /// <summary>Reads the newest entry without locking.</summary>
    /// <param name="context">The context.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The head, or <see cref="AuditChainHead.Empty"/>.</returns>
    public static async Task<AuditChainHead> ReadHeadAsync(RegReturnsDbContext context, CancellationToken cancellationToken)
    {
        var last = await context.AuditEntries.AsNoTracking()
            .OrderByDescending(e => e.Sequence)
            .Select(e => new { e.Sequence, e.Hash })
            .FirstOrDefaultAsync(cancellationToken);
        return last is null ? AuditChainHead.Empty : new AuditChainHead(last.Sequence, last.Hash);
    }
}
