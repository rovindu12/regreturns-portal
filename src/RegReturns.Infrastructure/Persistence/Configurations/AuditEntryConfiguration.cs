using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using RegReturns.Domain.Auditing;

namespace RegReturns.Infrastructure.Persistence.Configurations;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    /// <summary>The INSTEAD OF UPDATE, DELETE trigger created by the migration; declared so EF avoids OUTPUT clauses.</summary>
    public const string AppendOnlyTrigger = "TR_AuditEntries_AppendOnly";

    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries", Schemas.Audit, t => t.HasTrigger(AppendOnlyTrigger));

        // The sequence is assigned under the chain lock, not by an identity column, so it can be hashed.
        builder.HasKey(e => e.Sequence);
        builder.Property(e => e.Sequence).ValueGeneratedNever();
        builder.Property(e => e.ActorSubjectId).HasMaxLength(AuditEntry.SubjectMaxLength);
        builder.Property(e => e.ActorDisplayName).HasMaxLength(AuditEntry.DisplayNameMaxLength);
        builder.Property(e => e.InstitutionCode).HasMaxLength(10);
        builder.Property(e => e.EntityType).HasMaxLength(AuditEntry.EntityMaxLength);
        builder.Property(e => e.EntityId).HasMaxLength(AuditEntry.EntityMaxLength);
        builder.Property(e => e.Details).HasMaxLength(AuditEntry.DetailsMaxLength);
        builder.Property(e => e.IpAddress).HasMaxLength(AuditEntry.IpAddressMaxLength).IsUnicode(false);
        builder.Property(e => e.CorrelationId).HasMaxLength(AuditEntry.CorrelationIdMaxLength).IsUnicode(false);
        builder.Property(e => e.PreviousHash).HasMaxLength(AuditEntry.HashLength).IsFixedLength().IsUnicode(false);
        builder.Property(e => e.Hash).HasMaxLength(AuditEntry.HashLength).IsFixedLength().IsUnicode(false);

        // A change document can be long (a template with its fields and rules) and is never searched: no length limit,
        // overriding the 400-character convention for strings, so the column is nvarchar(max).
        builder.Property(e => e.Changes).Metadata.SetMaxLength(null);
        builder.HasIndex(e => e.OccurredAt);
        builder.HasIndex(e => new { e.ActorSubjectId, e.OccurredAt });
        builder.HasIndex(e => new { e.EntityType, e.EntityId });
    }
}
