using RegReturns.Domain.Common;

namespace RegReturns.Application.Idempotency;

/// <summary>
/// Remembers requests made with an <c>Idempotency-Key</c> and their responses, so a client can retry a request safely
/// (ADR 0027). Keys are scoped to the client that sent them.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Claims a key for a request. The first request with a key starts it; a retry with the same fingerprint gets the
    /// stored response, or waits while the first request is still running; a different fingerprint is refused.
    /// </summary>
    /// <param name="request">The client, key and request fingerprint.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>What to do with the request.</returns>
    Task<IdempotencyStart> BeginAsync(IdempotentRequest request, CancellationToken cancellationToken);

    /// <summary>Stores the response of a request this caller started, for replay until the record expires.</summary>
    /// <param name="recordId">The record id from <see cref="IdempotencyStart.RecordId"/>.</param>
    /// <param name="response">The response to replay.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the response is stored.</returns>
    Task CompleteAsync(Guid recordId, StoredResponse response, CancellationToken cancellationToken);

    /// <summary>
    /// Forgets a request whose answer must not be replayed (403, 409, 429 or a server error), so the client can retry it
    /// with the same key.
    /// </summary>
    /// <param name="recordId">The record id from <see cref="IdempotencyStart.RecordId"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the record is gone.</returns>
    Task ReleaseAsync(Guid recordId, CancellationToken cancellationToken);

    /// <summary>Deletes expired records.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The number of records deleted.</returns>
    Task<int> PurgeExpiredAsync(CancellationToken cancellationToken);
}

/// <summary>A request that carries an idempotency key.</summary>
/// <param name="ClientId">The OAuth client that sent it.</param>
/// <param name="Key">The <c>Idempotency-Key</c> header value.</param>
/// <param name="Fingerprint">The lower-case hex SHA-256 of the request's method, path, query and body.</param>
public sealed record IdempotentRequest(string ClientId, string Key, string Fingerprint);

/// <summary>A response kept for replay.</summary>
/// <param name="StatusCode">The HTTP status code.</param>
/// <param name="ContentType">The <c>Content-Type</c> header, if any.</param>
/// <param name="Location">The <c>Location</c> header, if any.</param>
/// <param name="Body">The response body.</param>
public sealed record StoredResponse(int StatusCode, string? ContentType, string? Location, byte[] Body);

/// <summary>What happens to a request with an idempotency key.</summary>
public enum IdempotencyOutcome
{
    /// <summary>The key is new (or expired, or abandoned by a crashed request): run the request.</summary>
    Started = 1,

    /// <summary>The same request already ran: send its stored response.</summary>
    Replay = 2,

    /// <summary>The same request is still running: ask the client to retry shortly.</summary>
    InProgress = 3,

    /// <summary>The key was used for a different request: refuse.</summary>
    KeyReused = 4,
}

/// <summary>The outcome of <see cref="IIdempotencyStore.BeginAsync"/>.</summary>
/// <param name="Outcome">What to do with the request.</param>
/// <param name="RecordId">The record to complete or release, when the request was started.</param>
/// <param name="Response">The stored response, when it is a replay.</param>
public sealed record IdempotencyStart(IdempotencyOutcome Outcome, Guid? RecordId = null, StoredResponse? Response = null)
{
    /// <summary>Gets the outcome for a request that is still running.</summary>
    public static IdempotencyStart InProgress { get; } = new(IdempotencyOutcome.InProgress);

    /// <summary>Gets the outcome for a key that was used for another request.</summary>
    public static IdempotencyStart KeyReused { get; } = new(IdempotencyOutcome.KeyReused);

    /// <summary>Starts the request.</summary>
    /// <param name="recordId">The record to complete or release.</param>
    /// <returns>The outcome.</returns>
    public static IdempotencyStart Started(Guid recordId) => new(IdempotencyOutcome.Started, recordId);

    /// <summary>Replays a stored response.</summary>
    /// <param name="response">The stored response.</param>
    /// <returns>The outcome.</returns>
    public static IdempotencyStart Replay(StoredResponse response) => new(IdempotencyOutcome.Replay, Response: response);
}

/// <summary>Refusals of requests with idempotency keys (ADR 0027).</summary>
public static class IdempotencyErrors
{
    /// <summary>The longest key accepted.</summary>
    public const int KeyMaxLength = 255;

    /// <summary>A POST without an <c>Idempotency-Key</c> header.</summary>
    public static readonly Error KeyRequired = new(
        "Idempotency.KeyRequired", "Send an Idempotency-Key header with a value unique to this request, such as a UUID.");

    /// <summary>A key that is empty, too long or not visible ASCII.</summary>
    public static readonly Error KeyInvalid = new(
        "Idempotency.KeyInvalid", "The Idempotency-Key must be 1 to 255 visible ASCII characters.");

    /// <summary>A key already used for a request with a different method, path or body.</summary>
    public static readonly Error KeyReused = new(
        "Idempotency.KeyReused", "This Idempotency-Key was already used for a different request. Use a new key for a new request.");

    /// <summary>A retry while the first request with the key is still running.</summary>
    public static readonly Error InProgress = new(
        "Idempotency.InProgress", "A request with this Idempotency-Key is still being processed. Retry shortly.");

    /// <summary>Returns whether a header value is a usable key.</summary>
    /// <param name="key">The header value.</param>
    /// <returns><see langword="true"/> if the key is 1 to 255 visible ASCII characters.</returns>
    public static bool IsValidKey(string? key) =>
        key is { Length: > 0 and <= KeyMaxLength } && key.All(c => c is >= '!' and <= '~');
}
