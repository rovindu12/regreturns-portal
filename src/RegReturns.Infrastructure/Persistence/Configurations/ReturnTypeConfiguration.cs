using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using RegReturns.Domain.Templates;

namespace RegReturns.Infrastructure.Persistence.Configurations;

internal sealed class ReturnTypeConfiguration : IEntityTypeConfiguration<ReturnType>
{
    public void Configure(EntityTypeBuilder<ReturnType> builder)
    {
        builder.ToTable("ReturnTypes", Schemas.Reference);
        builder.HasDomainKey();
        builder.HasRowVersion();
        builder.Property(r => r.Code).HasMaxLength(ReturnType.CodeMaxLength);
        builder.Property(r => r.Name).HasMaxLength(ReturnType.NameMaxLength);
        builder.Property(r => r.Description).HasMaxLength(ReturnType.DescriptionMaxLength);
        builder.HasIndex(r => r.Code).IsUnique();
    }
}
