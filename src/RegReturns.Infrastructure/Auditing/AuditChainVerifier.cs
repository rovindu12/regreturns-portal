using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using RegReturns.Application.Auditing;
using RegReturns.Application.Diagnostics;
using RegReturns.Domain.Auditing;
using RegReturns.Infrastructure.Persistence;

namespace RegReturns.Infrastructure.Auditing;

/// <summary>
/// Checks entries one at a time, in sequence order, and classifies the first break: a gap in the sequence numbers
/// (deleted entries), an entry that does not carry the previous entry's hash (re-ordered or replaced entries), or an
/// entry whose hash does not match its contents (an edited entry). The link is checked before the hash so that an
/// entry moved to another position reads as re-ordered rather than edited.
/// </summary>
/// <param name="hasher">The keyed hasher.</param>
internal sealed class AuditChainWalker(AuditHasher hasher)
{
    private long _expected = 1;
    private string _previousHash = AuditEntry.GenesisHash;

    /// <summary>Gets the number of entries checked so far.</summary>
    public long Checked { get; private set; }

    /// <summary>Checks the next entry.</summary>
    /// <param name="entry">The entry with the next-higher sequence number.</param>
    /// <returns>The break it reveals, or <see langword="null"/> if the chain is intact so far.</returns>
    public ChainBreak? Check(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Checked++;
        var expected = _expected;
        var previousHash = _previousHash;
        _expected = entry.Sequence + 1;
        _previousHash = entry.Hash;

        if (entry.Sequence != expected)
        {
            return ChainBreak.Missing(expected, entry.Sequence);
        }

        if (!string.Equals(entry.PreviousHash, previousHash, StringComparison.Ordinal))
        {
            return ChainBreak.Unlinked(entry.Sequence);
        }

        return hasher.Verify(entry) ? null : ChainBreak.Edited(entry.Sequence);
    }
}

/// <summary>
/// Verifies the chain in batches on a dedicated, untracked context, up to the head present when it starts, and logs
/// the outcome. An intact result logs the head sequence and hash: entries deleted from the end of the chain leave no
/// gap, so comparing a later head with a logged one is the only way to notice them (ADR 0024).
/// </summary>
/// <param name="contextOptions">Options for a dedicated context.</param>
/// <param name="hasher">The keyed hasher.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class AuditChainVerifier(
    DbContextOptions<RegReturnsDbContext> contextOptions,
    AuditHasher hasher,
    ILogger<AuditChainVerifier> logger) : IAuditChainVerifier
{
    /// <summary>The number of entries read per batch by default.</summary>
    public const int DefaultBatchSize = 500;

    /// <summary>The span around a verification.</summary>
    public const string ActivityName = "audit.verify-chain";

    /// <summary>Gets the number of entries read per batch.</summary>
    public int BatchSize { get; init; } = DefaultBatchSize;

    /// <inheritdoc />
    public async Task<ChainVerification> VerifyAsync(CancellationToken cancellationToken)
    {
        using var activity = RegReturnsTelemetry.ActivitySource.StartActivity(ActivityName);
        await using var db = new RegReturnsDbContext(contextOptions);
        var head = await AuditChain.ReadHeadAsync(db, cancellationToken);
        var walker = new AuditChainWalker(hasher);
        ChainBreak? broken = null;
        var after = 0L;
        while (broken is null && after < head.Sequence)
        {
            var batch = await db.AuditEntries.AsNoTracking()
                .Where(e => e.Sequence > after && e.Sequence <= head.Sequence)
                .OrderBy(e => e.Sequence)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var entry in batch)
            {
                broken = walker.Check(entry);
                if (broken is not null)
                {
                    break;
                }
            }

            after = batch[^1].Sequence;
        }

        var result = new ChainVerification(walker.Checked, head.Sequence, head.Sequence == 0 ? null : head.Hash, broken);
        activity?.SetTag("regreturns.audit.entries_checked", result.EntriesChecked);
        activity?.SetTag("regreturns.audit.intact", result.IsIntact);
        if (broken is null)
        {
            LogChainIntact(logger, result.EntriesChecked, result.HeadSequence, result.HeadHash);
        }
        else
        {
            activity?.SetTag("regreturns.audit.break_kind", broken.Kind.ToString());
            LogChainBroken(logger, broken.Sequence, broken.Kind, result.EntriesChecked, result.HeadSequence, result.HeadHash);
        }

        return result;
    }

    [LoggerMessage(EventId = 3003, Level = LogLevel.Information,
        Message = "Audit chain intact: {EntriesChecked} entries checked, head sequence {HeadSequence}, head hash {HeadHash}")]
    private static partial void LogChainIntact(ILogger logger, long entriesChecked, long headSequence, string? headHash);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Error,
        Message = "Audit chain broken at sequence {Sequence} ({BreakKind}) after {EntriesChecked} entries checked; head sequence {HeadSequence}, head hash {HeadHash}")]
    private static partial void LogChainBroken(
        ILogger logger, long sequence, ChainBreakKind breakKind, long entriesChecked, long headSequence, string? headHash);
}
