using System.Globalization;

using RegReturns.Domain.Auditing;
using RegReturns.Domain.Common;

namespace RegReturns.UnitTests.Auditing;

public sealed class AuditEntryTests
{
    private const char Separator = '\u001F';

    private static readonly string PreviousHash = new('b', AuditEntry.HashLength);

    // Pinned on purpose: changing the canonical form breaks verification of every chain already stored.
    private static readonly string PinnedCanonicalForm = string.Join(
        Separator,
        "7",
        "2026-10-04T09:30:15.0000000Z",
        "3f2b9c1e-0d4a-4f6b-9e8c-2a7d5b1c0e9f",
        "Nadia Fernhill",
        "User",
        "ALPHA",
        "AccessDenied",
        "Submission",
        "0199b3c4-1d2e-7f00-8a9b-0c1d2e3f4a5b",
        "path=/submissions/42; reason=Bank.SubmitReturn",
        "10.1.2.3",
        "4bf92f3577b34da6a3ce929d0e0e4736",
        new string('a', 64));

    [Fact]
    public void Genesis_hash_is_sixty_four_zeros()
    {
        AuditEntry.GenesisHash.ShouldBe(new string('0', 64));
    }

    [Fact]
    public void New_entry_is_unsealed_and_points_at_the_genesis_hash()
    {
        var entry = AuditTestData.Complete.Create();

        entry.Sequence.ShouldBe(0);
        entry.Hash.ShouldBeEmpty();
        entry.PreviousHash.ShouldBe(AuditEntry.GenesisHash);
    }

    [Fact]
    public void Create_stores_the_time_in_utc()
    {
        var local = new DateTimeOffset(2026, 10, 4, 15, 0, 0, TimeSpan.FromHours(5.5));

        var entry = AuditEntry.Create(local, AuditAction.SignIn, ActorType.User, "user-1");

        entry.OccurredAt.Offset.ShouldBe(TimeSpan.Zero);
        entry.OccurredAt.ShouldBe(new DateTimeOffset(2026, 10, 4, 9, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Create_keeps_every_field()
    {
        var fields = AuditTestData.Complete;

        var entry = fields.Create();

        entry.OccurredAt.ShouldBe(fields.OccurredAt);
        entry.Action.ShouldBe(fields.Action);
        entry.ActorType.ShouldBe(fields.ActorType);
        entry.ActorSubjectId.ShouldBe(fields.Subject);
        entry.ActorDisplayName.ShouldBe(fields.DisplayName);
        entry.InstitutionCode.ShouldBe(fields.InstitutionCode);
        entry.EntityType.ShouldBe(fields.EntityType);
        entry.EntityId.ShouldBe(fields.EntityId);
        entry.Details.ShouldBe(fields.Details);
        entry.IpAddress.ShouldBe(fields.IpAddress);
        entry.CorrelationId.ShouldBe(fields.CorrelationId);
    }

    [Fact]
    public void Create_truncates_optional_text_to_the_column_lengths()
    {
        var entry = AuditEntry.Create(
            AuditTestData.OccurredAt,
            AuditAction.AccessDenied,
            ActorType.User,
            "user-1",
            actorDisplayName: new string('n', 300),
            institutionCode: "ABCDEFGHIJKLMNOP",
            entityType: new string('t', 300),
            entityId: new string('i', 300),
            details: new string('d', 3000),
            ipAddress: new string('1', 100),
            correlationId: new string('c', 100));

        entry.ActorDisplayName.ShouldBe(new string('n', AuditEntry.DisplayNameMaxLength));
        entry.InstitutionCode.ShouldBe("ABCDEFGHIJ");
        entry.EntityType.ShouldBe(new string('t', AuditEntry.EntityMaxLength));
        entry.EntityId.ShouldBe(new string('i', AuditEntry.EntityMaxLength));
        entry.Details.ShouldBe(new string('d', AuditEntry.DetailsMaxLength));
        entry.IpAddress.ShouldBe(new string('1', AuditEntry.IpAddressMaxLength));
        entry.CorrelationId.ShouldBe(new string('c', AuditEntry.CorrelationIdMaxLength));
    }

    [Fact]
    public void Create_keeps_text_that_is_exactly_the_maximum_length()
    {
        var details = new string('d', AuditEntry.DetailsMaxLength);

        var entry = AuditEntry.Create(AuditTestData.OccurredAt, AuditAction.AccessDenied, ActorType.User, "user-1", details: details);

        entry.Details.ShouldBe(details);
    }

    [Fact]
    public void Create_stores_blank_optional_text_as_null()
    {
        var entry = AuditEntry.Create(
            AuditTestData.OccurredAt,
            AuditAction.SignOut,
            ActorType.User,
            "user-1",
            actorDisplayName: " ",
            institutionCode: string.Empty,
            entityType: "\t",
            entityId: "  ",
            details: string.Empty,
            ipAddress: " ",
            correlationId: string.Empty);

        new[] { entry.ActorDisplayName, entry.InstitutionCode, entry.EntityType, entry.EntityId, entry.Details, entry.IpAddress, entry.CorrelationId }
            .ShouldAllBe(value => value == null);
    }

    [Fact]
    public void Create_trims_the_actor_subject()
    {
        AuditEntry.Create(AuditTestData.OccurredAt, AuditAction.SignIn, ActorType.User, "  user-1 ").ActorSubjectId.ShouldBe("user-1");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_requires_an_actor_subject(string subject)
    {
        Should.Throw<DomainException>(() => AuditEntry.Create(AuditTestData.OccurredAt, AuditAction.SignIn, ActorType.User, subject));
    }

    [Fact]
    public void Create_rejects_an_actor_subject_longer_than_the_column()
    {
        var subject = new string('s', AuditEntry.SubjectMaxLength + 1);

        Should.Throw<DomainException>(() => AuditEntry.Create(AuditTestData.OccurredAt, AuditAction.SignIn, ActorType.User, subject));
    }

    [Fact]
    public void Create_replaces_control_characters_in_text_with_spaces()
    {
        var entry = AuditEntry.Create(
            AuditTestData.OccurredAt, AuditAction.AccessDenied, ActorType.User, "user-1", details: $"path=/a{Separator}b\r\nreason=x");

        entry.Details.ShouldBe("path=/a b  reason=x");
    }

    [Fact]
    public void Seal_places_the_entry_in_the_chain_with_the_hash_of_its_canonical_form()
    {
        var entry = AuditTestData.Complete.Create();
        string? hashed = null;

        entry.Seal(3, PreviousHash, canonical =>
        {
            hashed = canonical;
            return "computed-hash";
        });

        entry.Sequence.ShouldBe(3);
        entry.PreviousHash.ShouldBe(PreviousHash);
        entry.Hash.ShouldBe("computed-hash");
        hashed.ShouldBe(entry.ToCanonicalString());
    }

    [Fact]
    public void Seal_can_only_happen_once()
    {
        var entry = AuditTestData.Complete.Create();
        entry.Seal(1, AuditEntry.GenesisHash, _ => "first");

        Should.Throw<DomainException>(() => entry.Seal(2, PreviousHash, _ => "second"));
        entry.Hash.ShouldBe("first");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Seal_rejects_a_sequence_below_one(long sequence)
    {
        var entry = AuditTestData.Complete.Create();

        Should.Throw<DomainException>(() => entry.Seal(sequence, AuditEntry.GenesisHash, _ => "hash"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Seal_requires_a_previous_hash(string previousHash)
    {
        var entry = AuditTestData.Complete.Create();

        Should.Throw<DomainException>(() => entry.Seal(1, previousHash, _ => "hash"));
    }

    [Fact]
    public void Seal_requires_a_hash_function()
    {
        var entry = AuditTestData.Complete.Create();

        Should.Throw<ArgumentNullException>(() => entry.Seal(1, AuditEntry.GenesisHash, null!));
    }

    [Fact]
    public void Canonical_form_lists_every_field_in_a_fixed_order()
    {
        var entry = AuditTestData.Complete.Seal(_ => "hash");

        entry.ToCanonicalString().ShouldBe(PinnedCanonicalForm);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Entry_without_changes_has_the_canonical_form_entries_had_before_changes_were_recorded(string? changes)
    {
        // Phase 2 entries have no change document; their hashes must still verify (ADR 0024).
        var entry = (AuditTestData.Complete with { Changes = changes }).Seal(_ => "hash");

        entry.Changes.ShouldBeNull();
        entry.ToCanonicalString().ShouldBe(PinnedCanonicalForm);
    }

    [Fact]
    public void Entry_without_changes_hashes_exactly_as_before_changes_were_recorded()
    {
        // The HMAC of the pinned canonical form under AuditTestData.Key, computed before the change document existed.
        var hasher = AuditTestData.Hasher(AuditTestData.Key);

        AuditTestData.Complete.Seal(hasher.Compute).Hash.ShouldBe("49b9b1fb124259e23edbe231a0cf21f5fb39beef3b0cbdce535eeb0c32b60ca5");
    }

    [Fact]
    public void Changes_follow_the_previous_hash_in_the_canonical_form()
    {
        var fields = AuditTestData.WithChanges;

        var entry = fields.Seal(_ => "hash");

        entry.ToCanonicalString().ShouldBe(
            PinnedCanonicalForm.Replace("AccessDenied", "Updated", StringComparison.Ordinal) + Separator + fields.Changes);
    }

    [Fact]
    public void Create_keeps_the_change_document()
    {
        AuditTestData.WithChanges.Create().Changes.ShouldBe(AuditTestData.WithChanges.Changes);
    }

    [Fact]
    public void Create_replaces_control_characters_in_the_change_document_with_spaces()
    {
        var entry = AuditEntry.Create(
            AuditTestData.OccurredAt, AuditAction.Updated, ActorType.User, "user-1", changes: $"[{Separator}]");

        entry.Changes.ShouldBe("[ ]");
    }

    [Fact]
    public void Canonical_form_writes_missing_optional_fields_as_empty()
    {
        var entry = AuditEntry.Create(AuditTestData.OccurredAt, AuditAction.SignIn, ActorType.System, "system");

        entry.ToCanonicalString().ShouldBe(
            $"0{Separator}2026-10-04T09:30:15.0000000Z{Separator}system{Separator}{Separator}System{Separator}{Separator}SignIn"
            + $"{Separator}{Separator}{Separator}{Separator}{Separator}{Separator}{AuditEntry.GenesisHash}");
    }

    [Fact]
    public void Canonical_form_is_the_same_in_every_culture()
    {
        var entry = AuditTestData.Complete.Seal(_ => "hash");
        var invariant = entry.ToCanonicalString();
        var original = CultureInfo.CurrentCulture;

        try
        {
            foreach (var name in new[] { "th-TH", "ar-SA", "de-DE", "fa-IR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                entry.ToCanonicalString().ShouldBe(invariant, $"culture {name}");
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Canonical_form_keeps_text_on_its_own_side_of_a_field_boundary()
    {
        // Without sanitising, "Sub|mission" + "0199" and "Sub" + "mission|0199" would hash identically,
        // letting someone with database access move text between fields undetected.
        var original = AuditTestData.Complete with { EntityType = $"Sub{Separator}mission", EntityId = "0199" };
        var shifted = AuditTestData.Complete with { EntityType = "Sub", EntityId = $"mission{Separator}0199" };

        original.Create().ToCanonicalString().ShouldNotBe(shifted.Create().ToCanonicalString());
    }
}
