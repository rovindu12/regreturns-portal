using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

using RegReturns.Domain.Common;

namespace RegReturns.Domain.Auditing;

/// <summary>
/// One link in the tamper-evident audit chain. Entries are append-only: each carries the hash of the
/// previous entry, and its own hash covers its fields plus that previous hash (ADR 0016).
/// </summary>
public sealed class AuditEntry
{
    /// <summary>Maximum length of a subject id.</summary>
    public const int SubjectMaxLength = 128;

    /// <summary>Maximum length of a display name.</summary>
    public const int DisplayNameMaxLength = 128;

    /// <summary>Maximum length of an entity type or id.</summary>
    public const int EntityMaxLength = 128;

    /// <summary>Maximum length of the free-text details.</summary>
    public const int DetailsMaxLength = 1000;

    /// <summary>Maximum length of an IP address.</summary>
    public const int IpAddressMaxLength = 45;

    /// <summary>Maximum length of a correlation (W3C trace) id.</summary>
    public const int CorrelationIdMaxLength = 64;

    /// <summary>Length of a hex-encoded SHA-256 hash.</summary>
    public const int HashLength = 64;

    /// <summary>The previous hash of the first entry in a chain.</summary>
    public static readonly string GenesisHash = new('0', HashLength);

    private AuditEntry()
    {
        ActorSubjectId = string.Empty;
        PreviousHash = GenesisHash;
        Hash = string.Empty;
    }

    /// <summary>Gets the position in the chain, starting at 1, without gaps.</summary>
    public long Sequence { get; private set; }

    /// <summary>Gets when the action happened (UTC).</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Gets the WSO2 subject id, client id or <c>system</c>.</summary>
    public string ActorSubjectId { get; private set; }

    /// <summary>Gets the actor's display name at the time, if known.</summary>
    public string? ActorDisplayName { get; private set; }

    /// <summary>Gets the kind of actor.</summary>
    public ActorType ActorType { get; private set; }

    /// <summary>Gets the code of the actor's institution, if any.</summary>
    public string? InstitutionCode { get; private set; }

    /// <summary>Gets what happened.</summary>
    public AuditAction Action { get; private set; }

    /// <summary>Gets the type of the affected entity, if any.</summary>
    public string? EntityType { get; private set; }

    /// <summary>Gets the id of the affected entity, if any.</summary>
    public string? EntityId { get; private set; }

    /// <summary>Gets extra context such as the path and policy of a denied request. Never secrets or figures.</summary>
    public string? Details { get; private set; }

    /// <summary>Gets the caller's IP address.</summary>
    public string? IpAddress { get; private set; }

    /// <summary>Gets the W3C trace id of the request, joining the audit trail to the logs.</summary>
    public string? CorrelationId { get; private set; }

    /// <summary>Gets the hash of the previous entry (<see cref="GenesisHash"/> for the first).</summary>
    public string PreviousHash { get; private set; }

    /// <summary>Gets the keyed hash of this entry's canonical form and <see cref="PreviousHash"/>.</summary>
    public string Hash { get; private set; }

    /// <summary>
    /// Creates an unsealed entry; <see cref="Seal"/> must be called before it is stored. Control characters in
    /// text are replaced with spaces and optional text is truncated to its column length.
    /// </summary>
    /// <param name="occurredAt">When the action happened.</param>
    /// <param name="action">What happened.</param>
    /// <param name="actorType">The kind of actor.</param>
    /// <param name="actorSubjectId">The subject id, client id or <c>system</c>.</param>
    /// <param name="actorDisplayName">The actor's display name, if known.</param>
    /// <param name="institutionCode">The actor's institution code, if any.</param>
    /// <param name="entityType">The affected entity type, if any.</param>
    /// <param name="entityId">The affected entity id, if any.</param>
    /// <param name="details">Extra non-sensitive context.</param>
    /// <param name="ipAddress">The caller's IP address.</param>
    /// <param name="correlationId">The W3C trace id.</param>
    /// <returns>The new entry.</returns>
    public static AuditEntry Create(
        DateTimeOffset occurredAt,
        AuditAction action,
        ActorType actorType,
        string actorSubjectId,
        string? actorDisplayName = null,
        string? institutionCode = null,
        string? entityType = null,
        string? entityId = null,
        string? details = null,
        string? ipAddress = null,
        string? correlationId = null) => new()
        {
            OccurredAt = occurredAt.ToUniversalTime(),
            Action = action,
            ActorType = actorType,
            ActorSubjectId = Guard.NotBlank(WithoutControlCharacters(actorSubjectId), SubjectMaxLength),
            ActorDisplayName = Truncate(actorDisplayName, DisplayNameMaxLength),
            InstitutionCode = Truncate(institutionCode, 10),
            EntityType = Truncate(entityType, EntityMaxLength),
            EntityId = Truncate(entityId, EntityMaxLength),
            Details = Truncate(details, DetailsMaxLength),
            IpAddress = Truncate(ipAddress, IpAddressMaxLength),
            CorrelationId = Truncate(correlationId, CorrelationIdMaxLength),
        };

    /// <summary>
    /// Returns the canonical text the hash covers: every field in a fixed order, separated by the ASCII
    /// unit separator, formatted culture-invariantly. Changing this breaks verification of existing chains.
    /// </summary>
    /// <returns>The canonical form.</returns>
    public string ToCanonicalString()
    {
        const char separator = '\u001F';
        var builder = new StringBuilder();
        builder.Append(Sequence.ToString(CultureInfo.InvariantCulture)).Append(separator)
            .Append(OccurredAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)).Append(separator)
            .Append(ActorSubjectId).Append(separator)
            .Append(ActorDisplayName).Append(separator)
            .Append(ActorType.ToString()).Append(separator)
            .Append(InstitutionCode).Append(separator)
            .Append(Action.ToString()).Append(separator)
            .Append(EntityType).Append(separator)
            .Append(EntityId).Append(separator)
            .Append(Details).Append(separator)
            .Append(IpAddress).Append(separator)
            .Append(CorrelationId).Append(separator)
            .Append(PreviousHash);
        return builder.ToString();
    }

    /// <summary>Places the entry in the chain. The hash must be computed over <see cref="ToCanonicalString"/>.</summary>
    /// <param name="sequence">The next sequence number.</param>
    /// <param name="previousHash">The hash of the previous entry.</param>
    /// <param name="computeHash">Computes the keyed hash of the canonical form.</param>
    public void Seal(long sequence, string previousHash, Func<string, string> computeHash)
    {
        ArgumentNullException.ThrowIfNull(computeHash);
        if (Hash.Length > 0)
        {
            throw new DomainException("An audit entry can only be sealed once.");
        }

        if (sequence < 1)
        {
            throw new DomainException("Audit sequence numbers start at 1.");
        }

        Sequence = sequence;
        PreviousHash = Guard.NotBlank(previousHash, HashLength);
        Hash = computeHash(ToCanonicalString());
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var clean = WithoutControlCharacters(value);
        return clean.Length <= maxLength ? clean : clean[..maxLength];
    }

    // The canonical form separates fields with a control character, so no field may contain one:
    // otherwise text could be moved across a field boundary without changing the hash.
    [return: NotNullIfNotNull(nameof(value))]
    private static string? WithoutControlCharacters(string? value) =>
        value is null || !value.Any(char.IsControl)
            ? value
            : string.Concat(value.Select(c => char.IsControl(c) ? ' ' : c));
}
