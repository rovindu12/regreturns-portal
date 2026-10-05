using System.ComponentModel.DataAnnotations;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RegReturns.Application.Idempotency;
using RegReturns.Infrastructure.Persistence;

namespace RegReturns.Infrastructure.Idempotency;

/// <summary>How long idempotency records are kept and held (configuration section <c>Api:Idempotency</c>).</summary>
public sealed class IdempotencyOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Api:Idempotency";

    /// <summary>Gets or sets how many hours a key and its response are kept.</summary>
    [Range(1, 168)]
    public int RetentionHours { get; set; } = 24;

    /// <summary>
    /// Gets or sets how many seconds a running request holds its key. A request still unfinished after that is treated
    /// as abandoned (the process stopped), and a retry with the same key runs it again.
    /// </summary>
    [Range(5, 600)]
    public int InFlightSeconds { get; set; } = 60;

    /// <summary>Gets or sets how many minutes pass between purges of expired records.</summary>
    [Range(1, 1440)]
    public int PurgeIntervalMinutes { get; set; } = 60;
}

/// <summary>
/// Keeps idempotency records in <c>api.IdempotencyRecords</c> (ADR 0027). Each call uses its own context without the
/// data-change auditor, so a record is never saved with, or audited as, the change it guards. Claims rely on the
/// unique index on client and key, and take-overs on conditional updates, so two requests can never both run a key.
/// </summary>
/// <param name="contextOptions">Options for a dedicated context.</param>
/// <param name="options">Retention and in-flight settings.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class IdempotencyStore(
    DbContextOptions<RegReturnsDbContext> contextOptions,
    IOptions<IdempotencyOptions> options,
    TimeProvider timeProvider,
    ILogger<IdempotencyStore> logger) : IIdempotencyStore
{
    // A claim can lose a race with a release (the first request failed and let go of the key); try again then.
    private const int MaxClaimAttempts = 3;

    private TimeSpan LockFor => TimeSpan.FromSeconds(options.Value.InFlightSeconds);

    private TimeSpan RetainFor => TimeSpan.FromHours(options.Value.RetentionHours);

    /// <inheritdoc />
    public async Task<IdempotencyStart> BeginAsync(IdempotentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        for (var attempt = 1; attempt <= MaxClaimAttempts; attempt++)
        {
            var start = await TryBeginAsync(request, cancellationToken);
            if (start is not null)
            {
                return start;
            }
        }

        return IdempotencyStart.InProgress;
    }

    /// <inheritdoc />
    public async Task CompleteAsync(Guid recordId, StoredResponse response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        var now = timeProvider.GetUtcNow();
        await using var db = new RegReturnsDbContext(contextOptions);
        await db.Set<IdempotencyRecord>()
            .Where(r => r.Id == recordId && r.CompletedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(r => r.CompletedAt, now)
                    .SetProperty(r => r.StatusCode, response.StatusCode)
                    .SetProperty(r => r.ContentType, Truncate(response.ContentType))
                    .SetProperty(r => r.Location, Truncate(response.Location))
                    .SetProperty(r => r.ResponseBody, response.Body),
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(Guid recordId, CancellationToken cancellationToken)
    {
        await using var db = new RegReturnsDbContext(contextOptions);
        await db.Set<IdempotencyRecord>()
            .Where(r => r.Id == recordId && r.CompletedAt == null)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var db = new RegReturnsDbContext(contextOptions);
        var deleted = await db.Set<IdempotencyRecord>().Where(r => r.ExpiresAt <= now).ExecuteDeleteAsync(cancellationToken);
        if (deleted > 0)
        {
            LogPurged(logger, deleted);
        }

        return deleted;
    }

    private static Task<IdempotencyRecord?> FindAsync(RegReturnsDbContext db, IdempotentRequest request, CancellationToken cancellationToken) =>
        db.Set<IdempotencyRecord>().AsNoTracking()
            .SingleOrDefaultAsync(r => r.ClientId == request.ClientId && r.Key == request.Key, cancellationToken);

    private static string? Truncate(string? value) =>
        value is { Length: > IdempotencyRecord.HeaderMaxLength } ? value[..IdempotencyRecord.HeaderMaxLength] : value;

    private static bool IsDuplicateKey(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };

    /// <summary>One attempt to claim the key; <see langword="null"/> when a concurrent release means "try again".</summary>
    private async Task<IdempotencyStart?> TryBeginAsync(IdempotentRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var db = new RegReturnsDbContext(contextOptions);

        // Look first, so a retry (the common repeat) never relies on a failed insert.
        var existing = await FindAsync(db, request, cancellationToken);
        if (existing is null)
        {
            var record = IdempotencyRecord.Claim(request, now, LockFor, RetainFor);
            await db.Set<IdempotencyRecord>().AddAsync(record, cancellationToken);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return IdempotencyStart.Started(record.Id);
            }
            catch (DbUpdateException exception) when (IsDuplicateKey(exception))
            {
                // A concurrent request with the same key claimed it first.
                db.ChangeTracker.Clear();
            }

            existing = await FindAsync(db, request, cancellationToken);
            if (existing is null)
            {
                return null;
            }
        }

        if (existing.ExpiresAt <= now)
        {
            // An expired key is free again: start over in place, unless another request did so first.
            var reset = await db.Set<IdempotencyRecord>()
                .Where(r => r.Id == existing.Id && r.ExpiresAt <= now)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(r => r.Fingerprint, request.Fingerprint)
                        .SetProperty(r => r.CreatedAt, now)
                        .SetProperty(r => r.LockedUntil, now + LockFor)
                        .SetProperty(r => r.ExpiresAt, now + RetainFor)
                        .SetProperty(r => r.CompletedAt, (DateTimeOffset?)null)
                        .SetProperty(r => r.StatusCode, (int?)null)
                        .SetProperty(r => r.ContentType, (string?)null)
                        .SetProperty(r => r.Location, (string?)null)
                        .SetProperty(r => r.ResponseBody, (byte[]?)null),
                    cancellationToken);
            return reset == 1 ? IdempotencyStart.Started(existing.Id) : null;
        }

        if (!string.Equals(existing.Fingerprint, request.Fingerprint, StringComparison.Ordinal))
        {
            return IdempotencyStart.KeyReused;
        }

        if (existing.ToStoredResponse() is { } stored)
        {
            return IdempotencyStart.Replay(stored);
        }

        if (existing.LockedUntil > now)
        {
            return IdempotencyStart.InProgress;
        }

        // The request that claimed the key stopped without an answer (the process ended): run it again.
        var takenOver = await db.Set<IdempotencyRecord>()
            .Where(r => r.Id == existing.Id && r.CompletedAt == null && r.LockedUntil <= now)
            .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.LockedUntil, now + LockFor), cancellationToken);
        if (takenOver == 1)
        {
            LogTakenOver(logger, request.ClientId, existing.CreatedAt);
            return IdempotencyStart.Started(existing.Id);
        }

        return IdempotencyStart.InProgress;
    }

    [LoggerMessage(EventId = 3305, Level = LogLevel.Information, Message = "Purged {Count} expired idempotency records")]
    private static partial void LogPurged(ILogger logger, int count);

    [LoggerMessage(EventId = 3306, Level = LogLevel.Warning,
        Message = "Client {ClientId} retried a request that stopped without an answer (claimed at {ClaimedAt}); running it again")]
    private static partial void LogTakenOver(ILogger logger, string clientId, DateTimeOffset claimedAt);
}
