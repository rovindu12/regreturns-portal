using Microsoft.Extensions.Options;

using RegReturns.Domain.Auditing;
using RegReturns.Infrastructure.Auditing;

namespace RegReturns.UnitTests.Auditing;

/// <summary>Keys and a complete set of entry fields shared by the audit tests.</summary>
internal static class AuditTestData
{
    public static readonly DateTimeOffset OccurredAt = new(2026, 10, 4, 9, 30, 15, TimeSpan.Zero);

    /// <summary>A 32-byte key (bytes 1..32), base64-encoded.</summary>
    public static readonly string Key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    /// <summary>A different 32-byte key (bytes 101..132), base64-encoded.</summary>
    public static readonly string OtherKey = Convert.ToBase64String(Enumerable.Range(101, 32).Select(i => (byte)i).ToArray());

    /// <summary>An entry with every optional field filled in.</summary>
    public static readonly EntryFields Complete = new(
        Sequence: 7,
        OccurredAt: OccurredAt,
        Action: AuditAction.AccessDenied,
        ActorType: ActorType.User,
        Subject: "3f2b9c1e-0d4a-4f6b-9e8c-2a7d5b1c0e9f",
        DisplayName: "Nadia Fernhill",
        InstitutionCode: "ALPHA",
        EntityType: "Submission",
        EntityId: "0199b3c4-1d2e-7f00-8a9b-0c1d2e3f4a5b",
        Details: "path=/submissions/42; reason=Bank.SubmitReturn",
        IpAddress: "10.1.2.3",
        CorrelationId: "4bf92f3577b34da6a3ce929d0e0e4736",
        PreviousHash: new string('a', AuditEntry.HashLength));

    public static AuditHasher Hasher(string key) => new(Options.Create(new AuditOptions { HmacKey = key }));
}

/// <summary>The inputs of an audit entry, so a test can change exactly one of them.</summary>
internal sealed record EntryFields(
    long Sequence,
    DateTimeOffset OccurredAt,
    AuditAction Action,
    ActorType ActorType,
    string Subject,
    string? DisplayName,
    string? InstitutionCode,
    string? EntityType,
    string? EntityId,
    string? Details,
    string? IpAddress,
    string? CorrelationId,
    string PreviousHash)
{
    public AuditEntry Create() => AuditEntry.Create(
        OccurredAt, Action, ActorType, Subject, DisplayName, InstitutionCode, EntityType, EntityId, Details, IpAddress, CorrelationId);

    public AuditEntry Seal(Func<string, string> computeHash)
    {
        var entry = Create();
        entry.Seal(Sequence, PreviousHash, computeHash);
        return entry;
    }
}
