using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using RegReturns.Domain.Templates;

namespace RegReturns.Infrastructure.Persistence.Configurations;

internal sealed class TemplateVersionConfiguration : IEntityTypeConfiguration<TemplateVersion>
{
    public void Configure(EntityTypeBuilder<TemplateVersion> builder)
    {
        builder.ToTable("TemplateVersions", Schemas.Reference);
        builder.HasDomainKey();
        builder.HasRowVersion();
        builder.HasIndex(t => new { t.ReturnTypeId, t.Version }).IsUnique();
        builder.HasOne<ReturnType>().WithMany().HasForeignKey(t => t.ReturnTypeId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(t => t.Fields).WithOne().HasForeignKey(f => f.TemplateVersionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.Fields).HasField("_fields").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(t => t.Rules).WithOne().HasForeignKey(r => r.TemplateVersionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.Rules).HasField("_rules").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class TemplateFieldConfiguration : IEntityTypeConfiguration<TemplateField>
{
    public void Configure(EntityTypeBuilder<TemplateField> builder)
    {
        builder.ToTable("TemplateFields", Schemas.Reference);
        builder.HasDomainKey();
        builder.Property(f => f.TemplateVersionId);
        builder.Property(f => f.Code).HasMaxLength(TemplateField.CodeMaxLength);
        builder.Property(f => f.Label).HasMaxLength(TemplateField.LabelMaxLength);
        builder.Property(f => f.Section).HasMaxLength(TemplateField.SectionMaxLength);
        builder.Property(f => f.Unit).HasMaxLength(TemplateField.UnitMaxLength);
        builder.HasIndex(f => new { f.TemplateVersionId, f.Code }).IsUnique();
    }
}

internal sealed class ValidationRuleConfiguration : IEntityTypeConfiguration<ValidationRule>
{
    public void Configure(EntityTypeBuilder<ValidationRule> builder)
    {
        builder.ToTable("ValidationRules", Schemas.Reference);
        builder.HasDomainKey();
        builder.Property(r => r.TemplateVersionId);
        builder.Property(r => r.Code).HasMaxLength(ValidationRule.CodeMaxLength);
        builder.Property(r => r.TargetFieldCode).HasMaxLength(TemplateField.CodeMaxLength);
        builder.Property(r => r.Message).HasMaxLength(ValidationRule.MessageMaxLength);
        builder.Property(r => r.LeftExpression).HasMaxLength(ValidationRule.ExpressionMaxLength);
        builder.Property(r => r.RightExpression).HasMaxLength(ValidationRule.ExpressionMaxLength);
        builder.HasIndex(r => new { r.TemplateVersionId, r.Code }).IsUnique();
    }
}
