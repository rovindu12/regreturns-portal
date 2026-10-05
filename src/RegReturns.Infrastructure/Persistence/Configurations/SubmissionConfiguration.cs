using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using RegReturns.Domain.Identity;
using RegReturns.Domain.Institutions;
using RegReturns.Domain.Obligations;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;

namespace RegReturns.Infrastructure.Persistence.Configurations;

internal sealed class SubmissionConfiguration : IEntityTypeConfiguration<Submission>
{
    public void Configure(EntityTypeBuilder<Submission> builder)
    {
        builder.ToTable("Submissions", Schemas.Returns);
        builder.HasDomainKey();
        builder.HasRowVersion();

        builder.HasOne<ReturnObligation>().WithMany().HasForeignKey(s => s.ObligationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Institution>().WithMany().HasForeignKey(s => s.InstitutionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ReturnType>().WithMany().HasForeignKey(s => s.ReturnTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TemplateVersion>().WithMany().HasForeignKey(s => s.TemplateVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(s => s.PreparedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(s => s.LastEditedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(s => s.SubmittedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(s => s.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(s => s.DecidedByUserId).OnDelete(DeleteBehavior.Restrict);

        // At most one live submission per obligation; a rejected one is history and the bank files again.
        builder.HasIndex(s => s.ObligationId)
            .IsUnique()
            .HasFilter($"[{nameof(Submission.Status)}] <> N'{nameof(SubmissionStatus.Rejected)}'")
            .HasDatabaseName("UX_Submissions_LiveObligation");
        builder.HasIndex(s => new { s.InstitutionId, s.Status });
        builder.HasIndex(s => new { s.Status, s.LastSubmittedAt });

        builder.HasMany(s => s.Values).WithOne().HasForeignKey(v => v.SubmissionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Values).HasField("_values").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(s => s.Findings).WithOne().HasForeignKey(f => f.SubmissionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Findings).HasField("_findings").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(s => s.Events).WithOne().HasForeignKey(e => e.SubmissionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Events).HasField("_events").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(s => s.CurrentFindings);
        builder.Ignore(s => s.IsEditable);
    }
}

internal sealed class SubmissionValueConfiguration : IEntityTypeConfiguration<SubmissionValue>
{
    public void Configure(EntityTypeBuilder<SubmissionValue> builder)
    {
        builder.ToTable("SubmissionValues", Schemas.Returns);
        builder.HasDomainKey();
        builder.Property(v => v.SubmissionId);
        builder.Property(v => v.FieldCode).HasMaxLength(TemplateField.CodeMaxLength);
        builder.Property(v => v.RawValue).HasMaxLength(SubmissionValue.RawValueMaxLength);
        builder.HasIndex(v => new { v.SubmissionId, v.FieldCode }).IsUnique();
    }
}

internal sealed class ValidationFindingConfiguration : IEntityTypeConfiguration<ValidationFinding>
{
    public void Configure(EntityTypeBuilder<ValidationFinding> builder)
    {
        builder.ToTable("ValidationFindings", Schemas.Returns);
        builder.HasDomainKey();
        builder.Property(f => f.SubmissionId);
        builder.Property(f => f.RuleCode).HasMaxLength(ValidationRule.CodeMaxLength);
        builder.Property(f => f.FieldCode).HasMaxLength(TemplateField.CodeMaxLength);
        builder.Property(f => f.Message).HasMaxLength(ValidationRule.MessageMaxLength);
        builder.Property(f => f.Justification).HasMaxLength(ValidationFinding.JustificationMaxLength);
        builder.HasOne<ValidationRule>().WithMany().HasForeignKey(f => f.RuleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(f => f.JustifiedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(f => new { f.SubmissionId, f.Revision });
        builder.Ignore(f => f.BlocksSubmission);
    }
}

internal sealed class WorkflowEventConfiguration : IEntityTypeConfiguration<WorkflowEvent>
{
    public void Configure(EntityTypeBuilder<WorkflowEvent> builder)
    {
        builder.ToTable("WorkflowEvents", Schemas.Returns);
        builder.HasDomainKey();
        builder.Property(e => e.SubmissionId);
        builder.Property(e => e.ActorDisplayName).HasMaxLength(AppUser.DisplayNameMaxLength);
        builder.Property(e => e.Comment).HasMaxLength(WorkflowEvent.CommentMaxLength);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(e => e.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.SubmissionId, e.OccurredAt });
    }
}

internal sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> builder)
    {
        builder.ToTable("StoredFiles", Schemas.Returns);
        builder.HasDomainKey();
        builder.Property(f => f.FileName).HasMaxLength(StoredFile.FileNameMaxLength);
        builder.Property(f => f.ContentType).HasMaxLength(StoredFile.ContentTypeMaxLength).IsUnicode(false);
        builder.Property(f => f.Sha256).HasMaxLength(StoredFile.Sha256Length).IsFixedLength().IsUnicode(false);
        builder.Property(f => f.Content).HasMaxLength(StoredFile.MaxSizeBytes);
        builder.HasOne<Submission>().WithMany().HasForeignKey(f => f.SubmissionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(f => f.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(f => new { f.SubmissionId, f.UploadedAt });
    }
}
