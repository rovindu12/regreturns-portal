using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using RegReturns.Application.Idempotency;
using RegReturns.Domain.Institutions;
using RegReturns.Infrastructure.Idempotency;

namespace RegReturns.Infrastructure.Persistence.Configurations;

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords", Schemas.Api);
        builder.HasDomainKey();
        builder.Property(r => r.ClientId).HasMaxLength(ApiClient.ClientIdMaxLength);
        builder.Property(r => r.Key).HasMaxLength(IdempotencyErrors.KeyMaxLength).IsUnicode(false);
        builder.Property(r => r.Fingerprint).HasMaxLength(IdempotencyRecord.FingerprintLength).IsFixedLength().IsUnicode(false);
        builder.Property(r => r.ContentType).HasMaxLength(IdempotencyRecord.HeaderMaxLength);
        builder.Property(r => r.Location).HasMaxLength(IdempotencyRecord.HeaderMaxLength);

        // A key belongs to the client that sent it: two banks may use the same key.
        builder.HasIndex(r => new { r.ClientId, r.Key }).IsUnique();
        builder.HasIndex(r => r.ExpiresAt);
    }
}
