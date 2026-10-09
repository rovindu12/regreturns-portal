using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using RegReturns.Domain.Identity;
using RegReturns.Domain.Insights;
using RegReturns.Domain.Submissions;

namespace RegReturns.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="ReturnInsight"/> to <c>returns.ReturnInsights</c> (ADR 0030).</summary>
internal sealed class ReturnInsightConfiguration : IEntityTypeConfiguration<ReturnInsight>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ReturnInsight> builder)
    {
        builder.ToTable("ReturnInsights", Schemas.Returns);
        builder.HasDomainKey();
        builder.Property(i => i.Model).HasMaxLength(ReturnInsight.ModelMaxLength).IsUnicode(false);
        builder.Property(i => i.InputSha256).HasMaxLength(ReturnInsight.Sha256Length).IsFixedLength().IsUnicode(false);
        builder.Property(i => i.OutputSha256).HasMaxLength(ReturnInsight.Sha256Length).IsFixedLength().IsUnicode(false);

        // JSON documents, never searched: nvarchar(max), overriding the 400-character convention for strings.
        builder.Property(i => i.Payload).Metadata.SetMaxLength(null);
        builder.Property(i => i.Content).Metadata.SetMaxLength(null);

        builder.HasOne<Submission>().WithMany().HasForeignKey(i => i.SubmissionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(i => i.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => new { i.SubmissionId, i.Revision, i.CreatedAt });
    }
}
