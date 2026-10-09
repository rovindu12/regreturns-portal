using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using RegReturns.Domain.Migration;

namespace RegReturns.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="MigrationRun"/>, its files and its row errors to the <c>migration</c> schema.</summary>
internal sealed class MigrationRunConfiguration : IEntityTypeConfiguration<MigrationRun>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MigrationRun> builder)
    {
        builder.ToTable("Runs", Schemas.Migration);
        builder.HasDomainKey();
        builder.Property(r => r.Source).HasMaxLength(MigrationRun.SourceMaxLength);
        builder.Property(r => r.MappingSha256).HasMaxLength(MigrationRun.Sha256Length).IsFixedLength().IsUnicode(false);
        builder.HasIndex(r => r.StartedAt);

        builder.OwnsMany(r => r.Files, files =>
        {
            files.ToTable("RunFiles", Schemas.Migration);
            files.WithOwner().HasForeignKey("MigrationRunId");
            files.Property<int>("Id");
            files.HasKey("MigrationRunId", "Id");
            files.Property(f => f.FileName).HasMaxLength(MigrationSourceFile.FileNameMaxLength);
            files.Property(f => f.ReturnTypeCode).HasMaxLength(MigrationRowError.CodeMaxLength).IsUnicode(false);
            files.Property(f => f.Sha256).HasMaxLength(MigrationSourceFile.Sha256Length).IsFixedLength().IsUnicode(false);

            // Also names each file in the audit change document, rather than its generated key.
            files.HasIndex("MigrationRunId", nameof(MigrationSourceFile.FileName)).IsUnique();
        });
        builder.Navigation(r => r.Files).HasField("_files").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(r => r.Errors).WithOne().HasForeignKey(e => e.MigrationRunId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Errors).HasField("_errors").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary>Maps <see cref="MigrationRowError"/>.</summary>
internal sealed class MigrationRowErrorConfiguration : IEntityTypeConfiguration<MigrationRowError>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MigrationRowError> builder)
    {
        builder.ToTable("RowErrors", Schemas.Migration);
        builder.HasDomainKey();
        builder.Property(e => e.MigrationRunId);
        builder.Property(e => e.FileName).HasMaxLength(MigrationRowError.FileNameMaxLength);
        builder.Property(e => e.Code).HasMaxLength(MigrationRowError.CodeMaxLength).IsUnicode(false);
        builder.Property(e => e.Field).HasMaxLength(MigrationRowError.FieldMaxLength);
        builder.Property(e => e.Message).HasMaxLength(MigrationRowError.MessageMaxLength);
        builder.Property(e => e.Value).HasMaxLength(MigrationRowError.ValueMaxLength);
        builder.HasIndex(e => new { e.MigrationRunId, e.FileName, e.LineNumber });
    }
}
