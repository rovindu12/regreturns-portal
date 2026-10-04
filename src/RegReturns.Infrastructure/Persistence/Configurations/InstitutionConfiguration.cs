using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using RegReturns.Domain.Institutions;

namespace RegReturns.Infrastructure.Persistence.Configurations;

internal sealed class InstitutionConfiguration : IEntityTypeConfiguration<Institution>
{
    public void Configure(EntityTypeBuilder<Institution> builder)
    {
        builder.ToTable("Institutions", Schemas.Reference);
        builder.HasDomainKey();
        builder.HasRowVersion();
        builder.Property(i => i.Code).HasMaxLength(Institution.CodeMaxLength);
        builder.Property(i => i.Name).HasMaxLength(Institution.NameMaxLength);
        builder.HasIndex(i => i.Code).IsUnique();
    }
}
