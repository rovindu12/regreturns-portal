using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Abstractions;
using RegReturns.Domain.Auditing;
using RegReturns.Domain.Common;
using RegReturns.Domain.Identity;
using RegReturns.Domain.Institutions;
using RegReturns.Domain.Obligations;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;

namespace RegReturns.Infrastructure.Persistence;

/// <summary>EF Core unit of work for the RegReturns database.</summary>
/// <param name="options">The context options.</param>
public sealed class RegReturnsDbContext(DbContextOptions<RegReturnsDbContext> options)
    : DbContext(options), IAppDbContext
{
    /// <summary>Maximum stored length of an enum name.</summary>
    internal const int EnumMaxLength = 40;

    /// <inheritdoc />
    public DbSet<Institution> Institutions => Set<Institution>();

    /// <inheritdoc />
    public DbSet<AppUser> Users => Set<AppUser>();

    /// <inheritdoc />
    public DbSet<ReturnType> ReturnTypes => Set<ReturnType>();

    /// <inheritdoc />
    public DbSet<TemplateVersion> TemplateVersions => Set<TemplateVersion>();

    /// <inheritdoc />
    public DbSet<ReturnObligation> Obligations => Set<ReturnObligation>();

    /// <inheritdoc />
    public DbSet<Submission> Submissions => Set<Submission>();

    /// <inheritdoc />
    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();

    /// <inheritdoc />
    public DbSet<ApiClient> ApiClients => Set<ApiClient>();

    /// <summary>Gets the audit chain. Append only through <see cref="Auditing.AuditTrail"/>.</summary>
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RegReturnsDbContext).Assembly);
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums are stored by name so the data reads clearly in SQL, reports and audit extracts.
        foreach (var enumType in typeof(Entity).Assembly.GetTypes().Where(t => t.IsEnum))
        {
            configurationBuilder.Properties(enumType).HaveConversion<string>().HaveMaxLength(EnumMaxLength);
        }

        configurationBuilder.Properties<decimal>().HavePrecision(19, 4);
        configurationBuilder.Properties<string>().HaveMaxLength(400);
    }
}
