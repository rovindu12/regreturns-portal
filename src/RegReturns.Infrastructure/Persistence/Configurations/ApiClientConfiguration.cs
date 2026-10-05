using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using RegReturns.Domain.Institutions;

namespace RegReturns.Infrastructure.Persistence.Configurations;

internal sealed class ApiClientConfiguration : IEntityTypeConfiguration<ApiClient>
{
    public void Configure(EntityTypeBuilder<ApiClient> builder)
    {
        builder.ToTable("ApiClients", Schemas.Identity);
        builder.HasDomainKey();
        builder.HasRowVersion();
        builder.Property(c => c.Wso2ClientId).HasMaxLength(ApiClient.ClientIdMaxLength).IsUnicode(false);
        builder.Property(c => c.Name).HasMaxLength(ApiClient.NameMaxLength);
        builder.HasIndex(c => c.Wso2ClientId).IsUnique();
        builder.HasOne<Institution>().WithMany().HasForeignKey(c => c.InstitutionId).OnDelete(DeleteBehavior.Restrict);
    }
}
