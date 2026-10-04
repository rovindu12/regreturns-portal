using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using RegReturns.Domain.Identity;
using RegReturns.Domain.Institutions;

namespace RegReturns.Infrastructure.Persistence.Configurations;

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("Users", Schemas.Identity);
        builder.HasDomainKey();
        builder.HasRowVersion();
        builder.Property(u => u.UserName).HasMaxLength(AppUser.UserNameMaxLength);
        builder.Property(u => u.DisplayName).HasMaxLength(AppUser.DisplayNameMaxLength);
        builder.Property(u => u.Email).HasMaxLength(AppUser.EmailMaxLength);
        builder.Property(u => u.Wso2UserId).HasMaxLength(64);

        // Roles are a small set of enum names stored as a JSON array, e.g. ["BankMaker"].
        builder.PrimitiveCollection(u => u.Roles)
            .HasField("_roles")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasMaxLength(400)
            .ElementType(role => role.HasConversion<string>().HasMaxLength(RegReturnsDbContext.EnumMaxLength));

        builder.HasIndex(u => u.UserName).IsUnique();
        builder.HasIndex(u => u.Wso2UserId).IsUnique().HasFilter("[Wso2UserId] IS NOT NULL");
        builder.HasOne<Institution>().WithMany().HasForeignKey(u => u.InstitutionId).OnDelete(DeleteBehavior.Restrict);
    }
}
