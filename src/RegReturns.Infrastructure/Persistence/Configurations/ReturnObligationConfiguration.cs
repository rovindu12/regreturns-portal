using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using RegReturns.Domain.Institutions;
using RegReturns.Domain.Obligations;
using RegReturns.Domain.Templates;

namespace RegReturns.Infrastructure.Persistence.Configurations;

internal sealed class ReturnObligationConfiguration : IEntityTypeConfiguration<ReturnObligation>
{
    public void Configure(EntityTypeBuilder<ReturnObligation> builder)
    {
        builder.ToTable("Obligations", Schemas.Returns);
        builder.HasDomainKey();
        builder.HasRowVersion();

        builder.ComplexProperty(o => o.Period, period =>
        {
            period.Property(p => p.Frequency).HasColumnName("PeriodFrequency");
            period.Property(p => p.Year).HasColumnName("PeriodYear");
            period.Property(p => p.Number).HasColumnName("PeriodNumber");
        });

        builder.HasOne<Institution>().WithMany().HasForeignKey(o => o.InstitutionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ReturnType>().WithMany().HasForeignKey(o => o.ReturnTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(o => new { o.Status, o.DueDate });

        // One obligation per bank, return type and period. EF Core cannot index complex-type columns,
        // so the unique index UX_Obligations_Institution_Return_Period is created in the InitialCreate migration.
    }
}
