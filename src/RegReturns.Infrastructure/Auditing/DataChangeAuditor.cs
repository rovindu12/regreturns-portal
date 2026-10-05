using Microsoft.EntityFrameworkCore.ChangeTracking;

using RegReturns.Application.Auditing;
using RegReturns.Domain.Auditing;

namespace RegReturns.Infrastructure.Auditing;

/// <summary>
/// Turns the changes a context is about to save into audit entries, one per aggregate root (ADR 0024). The hosts'
/// contexts get it through their second constructor; a context without it (the migrator, seeding, design time and
/// test helpers) writes no audit entries.
/// </summary>
public interface IDataChangeAuditor
{
    /// <summary>Describes the pending changes with the current actor and time.</summary>
    /// <param name="changeTracker">The context's change tracker, after change detection.</param>
    /// <returns>The entries to seal and save with the changes, or <see langword="null"/> when nothing audited changes.</returns>
    AuditBatch? Prepare(ChangeTracker changeTracker);
}

/// <summary>The audit entries for one save, waiting to be placed in the chain under its lock.</summary>
public sealed class AuditBatch
{
    private readonly Func<string, string> _computeHash;

    internal AuditBatch(IReadOnlyList<DataChange> changes, AuditOrigin origin, DateTimeOffset occurredAt, Func<string, string> computeHash)
    {
        Changes = changes;
        Origin = origin;
        OccurredAt = occurredAt;
        _computeHash = computeHash;
    }

    /// <summary>Gets the changes, one per aggregate root, in the order they are appended.</summary>
    public IReadOnlyList<DataChange> Changes { get; }

    /// <summary>Gets who made them and where the request came from.</summary>
    public AuditOrigin Origin { get; }

    /// <summary>Gets when they were made.</summary>
    public DateTimeOffset OccurredAt { get; }

    /// <summary>
    /// Creates the entries and seals them in order after the chain's head. Every call creates new entries, so a save
    /// that is retried after a transient failure seals afresh after whatever head it then finds.
    /// </summary>
    /// <param name="lastSequence">The sequence number of the newest entry, or 0 for an empty chain.</param>
    /// <param name="lastHash">The hash of the newest entry, or <see cref="AuditEntry.GenesisHash"/>.</param>
    /// <returns>The sealed entries, ready to add to the context.</returns>
    public IReadOnlyList<AuditEntry> Seal(long lastSequence, string lastHash)
    {
        var actor = Origin.Actor;
        var entries = new List<AuditEntry>(Changes.Count);
        foreach (var change in Changes)
        {
            var entry = AuditEntry.Create(
                OccurredAt, change.Action, actor.Type, actor.SubjectId, actor.DisplayName, actor.InstitutionCode,
                change.EntityType, change.EntityId, change.Details, Origin.IpAddress, Origin.CorrelationId, change.Changes);
            entry.Seal(++lastSequence, lastHash, _computeHash);
            lastHash = entry.Hash;
            entries.Add(entry);
        }

        return entries;
    }
}

/// <summary>Collects the pending changes and stamps them with the caller from <see cref="IAuditContext"/>.</summary>
/// <param name="auditContext">Who is acting.</param>
/// <param name="hasher">The keyed hasher.</param>
/// <param name="timeProvider">The clock.</param>
internal sealed class DataChangeAuditor(IAuditContext auditContext, AuditHasher hasher, TimeProvider timeProvider) : IDataChangeAuditor
{
    /// <inheritdoc />
    public AuditBatch? Prepare(ChangeTracker changeTracker)
    {
        var changes = DataChangeCollector.Collect(changeTracker);
        return changes.Count == 0 ? null : new AuditBatch(changes, auditContext.Current, timeProvider.GetUtcNow(), hasher.Compute);
    }
}
