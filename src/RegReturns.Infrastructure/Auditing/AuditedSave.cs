using System.Data;
using System.Diagnostics;

using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Diagnostics;
using RegReturns.Domain.Auditing;
using RegReturns.Infrastructure.Persistence;

namespace RegReturns.Infrastructure.Auditing;

/// <summary>
/// Saves a context's changes together with their audit entries, in one transaction (ADR 0024). The whole step runs in
/// the context's execution strategy, so a transient failure retries it from the start: entries sealed by the failed
/// attempt are dropped, the chain lock is taken again and the entries are sealed after the head it then finds. A save
/// inside a caller's transaction joins it; the lock is then held until the caller commits or rolls back. Any other
/// failure (a concurrency conflict, a unique index) reaches the caller unchanged and leaves no audit entry behind.
/// </summary>
internal static class AuditedSave
{
    /// <summary>The span around an audited save.</summary>
    public const string ActivityName = "audit.append-data-changes";

    /// <summary>Saves the changes and their entries.</summary>
    /// <param name="context">The context.</param>
    /// <param name="batch">The entries for the pending changes.</param>
    /// <param name="saveChanges">The context's own save, without auditing.</param>
    /// <param name="acceptAllChangesOnSuccess">Whether to accept the changes once committed.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The number of rows written, audit entries included.</returns>
    public static async Task<int> SaveAsync(
        RegReturnsDbContext context,
        AuditBatch batch,
        Func<bool, CancellationToken, Task<int>> saveChanges,
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken)
    {
        using var activity = StartActivity(batch);
        var added = new List<AuditEntry>(batch.Changes.Count);
        try
        {
            var rows = await context.Database.CreateExecutionStrategy().ExecuteAsync(
                (context, batch, saveChanges, added),
                static async (_, state, ct) =>
                {
                    var (db, audit, save, entries) = state;
                    Forget(db, entries);
                    await using var transaction = db.Database.CurrentTransaction is null
                        ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
                        : null;
                    var head = await AuditChain.LockAsync(db, ct);
                    entries.AddRange(audit.Seal(head.Sequence, head.Hash));
                    await db.AuditEntries.AddRangeAsync(entries, ct);
                    var written = await save(false, ct);
                    if (transaction is not null)
                    {
                        await transaction.CommitAsync(ct);
                    }

                    return written;
                },
                verifySucceeded: null,
                cancellationToken);
            Accept(context, acceptAllChangesOnSuccess);
            return rows;
        }
        finally
        {
            Forget(context, added);
        }
    }

    /// <summary>Saves the changes and their entries.</summary>
    /// <param name="context">The context.</param>
    /// <param name="batch">The entries for the pending changes.</param>
    /// <param name="saveChanges">The context's own save, without auditing.</param>
    /// <param name="acceptAllChangesOnSuccess">Whether to accept the changes once committed.</param>
    /// <returns>The number of rows written, audit entries included.</returns>
    public static int Save(RegReturnsDbContext context, AuditBatch batch, Func<bool, int> saveChanges, bool acceptAllChangesOnSuccess)
    {
        using var activity = StartActivity(batch);
        var added = new List<AuditEntry>(batch.Changes.Count);
        try
        {
            var rows = context.Database.CreateExecutionStrategy().Execute(
                (context, batch, saveChanges, added),
                static (_, state) =>
                {
                    var (db, audit, save, entries) = state;
                    Forget(db, entries);
                    using var transaction = db.Database.CurrentTransaction is null
                        ? db.Database.BeginTransaction(IsolationLevel.ReadCommitted)
                        : null;
                    var head = AuditChain.Lock(db);
                    entries.AddRange(audit.Seal(head.Sequence, head.Hash));
                    db.AuditEntries.AddRange(entries);
                    var written = save(false);
                    transaction?.Commit();
                    return written;
                },
                verifySucceeded: null);
            Accept(context, acceptAllChangesOnSuccess);
            return rows;
        }
        finally
        {
            Forget(context, added);
        }
    }

    private static Activity? StartActivity(AuditBatch batch)
    {
        var activity = RegReturnsTelemetry.ActivitySource.StartActivity(ActivityName);
        activity?.SetTag("regreturns.audit.entries", batch.Changes.Count);
        return activity;
    }

    private static void Accept(DbContext context, bool acceptAllChangesOnSuccess)
    {
        // Inside a caller's transaction this accepts before the caller commits, as EF Core itself does.
        if (acceptAllChangesOnSuccess)
        {
            context.ChangeTracker.AcceptAllChanges();
        }
    }

    // Entries are only ever written with the changes they describe: never left tracked for a later save to pick up.
    private static void Forget(DbContext context, List<AuditEntry> entries)
    {
        foreach (var entry in entries)
        {
            context.Entry(entry).State = EntityState.Detached;
        }

        entries.Clear();
    }
}
