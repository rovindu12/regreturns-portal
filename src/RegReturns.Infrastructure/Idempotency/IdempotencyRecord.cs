using System.Diagnostics.CodeAnalysis;

using RegReturns.Application.Idempotency;
using RegReturns.Domain.Auditing;
using RegReturns.Domain.Common;

namespace RegReturns.Infrastructure.Idempotency;

/// <summary>
/// One idempotency key of one API client: the fingerprint of the request that claimed it and, once that request
/// finished, its response (ADR 0027). Technical bookkeeping, so never audited: the change it guards is.
/// </summary>
[NotAudited]
[SuppressMessage(
    "Major Code Smell",
    "S1144:Unused private types or members should be removed",
    Justification = "EF Core sets the response columns when it reads a record; the store writes them with ExecuteUpdate.")]
internal sealed class IdempotencyRecord : Entity
{
    /// <summary>Length of a hex SHA-256 fingerprint.</summary>
    public const int FingerprintLength = 64;

    /// <summary>Maximum length of a stored <c>Content-Type</c> or <c>Location</c> header.</summary>
    public const int HeaderMaxLength = 400;

    private IdempotencyRecord()
    {
        ClientId = string.Empty;
        Key = string.Empty;
        Fingerprint = string.Empty;
    }

    /// <summary>Gets the OAuth client that owns the key.</summary>
    public string ClientId { get; private set; }

    /// <summary>Gets the key.</summary>
    public string Key { get; private set; }

    /// <summary>Gets the fingerprint of the request that claimed the key.</summary>
    public string Fingerprint { get; private set; }

    /// <summary>Gets when the key was claimed.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets until when an unfinished request holds the key; after that a retry may take it over.</summary>
    public DateTimeOffset LockedUntil { get; private set; }

    /// <summary>Gets when the record may be deleted and the key used again.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Gets when the response was stored, or <see langword="null"/> while the request runs.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Gets the stored status code.</summary>
    public int? StatusCode { get; private set; }

    /// <summary>Gets the stored <c>Content-Type</c> header.</summary>
    public string? ContentType { get; private set; }

    /// <summary>Gets the stored <c>Location</c> header.</summary>
    public string? Location { get; private set; }

    /// <summary>Gets the stored response body.</summary>
    public byte[]? ResponseBody { get; private set; }

    /// <summary>Claims a key for a request that is about to run.</summary>
    /// <param name="request">The request.</param>
    /// <param name="now">The current time.</param>
    /// <param name="lockFor">How long the request holds the key before a retry may take it over.</param>
    /// <param name="retainFor">How long the record is kept.</param>
    /// <returns>The record.</returns>
    public static IdempotencyRecord Claim(IdempotentRequest request, DateTimeOffset now, TimeSpan lockFor, TimeSpan retainFor) => new()
    {
        ClientId = request.ClientId,
        Key = request.Key,
        Fingerprint = request.Fingerprint,
        CreatedAt = now,
        LockedUntil = now + lockFor,
        ExpiresAt = now + retainFor,
    };

    /// <summary>Returns the stored response, or <see langword="null"/> while the request runs.</summary>
    /// <returns>The response to replay.</returns>
    public StoredResponse? ToStoredResponse() =>
        CompletedAt is null || StatusCode is not { } status
            ? null
            : new StoredResponse(status, ContentType, Location, ResponseBody ?? []);
}
