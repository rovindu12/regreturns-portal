using Microsoft.Extensions.Options;

using RegReturns.Domain.Auditing;
using RegReturns.Infrastructure.Auditing;

namespace RegReturns.UnitTests.Auditing;

public sealed class AuditHasherTests
{
    private readonly AuditHasher _hasher = AuditTestData.Hasher(AuditTestData.Key);

    public static TheoryData<string> Fields() =>
    [
        nameof(AuditEntry.Sequence),
        nameof(AuditEntry.OccurredAt),
        nameof(AuditEntry.Action),
        nameof(AuditEntry.ActorType),
        nameof(AuditEntry.ActorSubjectId),
        nameof(AuditEntry.ActorDisplayName),
        nameof(AuditEntry.InstitutionCode),
        nameof(AuditEntry.EntityType),
        nameof(AuditEntry.EntityId),
        nameof(AuditEntry.Details),
        nameof(AuditEntry.IpAddress),
        nameof(AuditEntry.CorrelationId),
        nameof(AuditEntry.PreviousHash),
    ];

    [Fact]
    public void Compute_is_hmac_sha256_in_lower_case_hex()
    {
        // RFC 4231, test case 2.
        var hasher = new AuditHasher(Options.Create(new AuditOptions { HmacKey = Convert.ToBase64String("Jefe"u8) }));

        hasher.Compute("what do ya want for nothing?")
            .ShouldBe("5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843");
    }

    [Fact]
    public void Compute_is_deterministic_for_the_same_key()
    {
        var canonical = AuditTestData.Complete.Create().ToCanonicalString();

        _hasher.Compute(canonical).ShouldBe(AuditTestData.Hasher(AuditTestData.Key).Compute(canonical));
    }

    [Fact]
    public void Compute_depends_on_the_key()
    {
        var canonical = AuditTestData.Complete.Create().ToCanonicalString();

        _hasher.Compute(canonical).ShouldNotBe(AuditTestData.Hasher(AuditTestData.OtherKey).Compute(canonical));
    }

    [Fact]
    public void Compute_rejects_null()
    {
        Should.Throw<ArgumentNullException>(() => _hasher.Compute(null!));
    }

    [Fact]
    public void Sealed_entry_verifies()
    {
        var entry = AuditTestData.Complete.Seal(_hasher.Compute);

        _hasher.Verify(entry).ShouldBeTrue();
    }

    [Fact]
    public void Entry_sealed_with_another_key_does_not_verify()
    {
        var entry = AuditTestData.Complete.Seal(AuditTestData.Hasher(AuditTestData.OtherKey).Compute);

        _hasher.Verify(entry).ShouldBeFalse();
    }

    [Fact]
    public void Unsealed_entry_does_not_verify()
    {
        _hasher.Verify(AuditTestData.Complete.Create()).ShouldBeFalse();
    }

    [Fact]
    public void Verify_rejects_null()
    {
        Should.Throw<ArgumentNullException>(() => _hasher.Verify(null!));
    }

    [Theory]
    [MemberData(nameof(Fields))]
    public void Changing_any_field_breaks_verification(string field)
    {
        var genuine = AuditTestData.Complete.Seal(_hasher.Compute);

        // The same entry with one field altered but carrying the genuine hash, as if edited in the database.
        var tampered = Tamper(field).Seal(_ => genuine.Hash);

        _hasher.Verify(tampered).ShouldBeFalse();
    }

    private static EntryFields Tamper(string field)
    {
        var original = AuditTestData.Complete;
        return field switch
        {
            nameof(AuditEntry.Sequence) => original with { Sequence = original.Sequence + 1 },
            nameof(AuditEntry.OccurredAt) => original with { OccurredAt = original.OccurredAt.AddTicks(1) },
            nameof(AuditEntry.Action) => original with { Action = AuditAction.AuthenticationFailed },
            nameof(AuditEntry.ActorType) => original with { ActorType = ActorType.ApiClient },
            nameof(AuditEntry.ActorSubjectId) => original with { Subject = "someone-else" },
            nameof(AuditEntry.ActorDisplayName) => original with { DisplayName = null },
            nameof(AuditEntry.InstitutionCode) => original with { InstitutionCode = "BETA" },
            nameof(AuditEntry.EntityType) => original with { EntityType = "Institution" },
            nameof(AuditEntry.EntityId) => original with { EntityId = "0199b3c4-1d2e-7f00-8a9b-0c1d2e3f4a5c" },
            nameof(AuditEntry.Details) => original with { Details = "path=/submissions/43; reason=Bank.SubmitReturn" },
            nameof(AuditEntry.IpAddress) => original with { IpAddress = "10.1.2.4" },
            nameof(AuditEntry.CorrelationId) => original with { CorrelationId = "00000000000000000000000000000001" },
            nameof(AuditEntry.PreviousHash) => original with { PreviousHash = new string('c', AuditEntry.HashLength) },
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown field."),
        };
    }
}
