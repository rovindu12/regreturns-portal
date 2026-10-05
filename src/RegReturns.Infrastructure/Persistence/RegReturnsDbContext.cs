using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Abstractions;
using RegReturns.Domain.Auditing;
using RegReturns.Domain.Common;
using RegReturns.Domain.Identity;
using RegReturns.Domain.Institutions;
using RegReturns.Domain.Obligations;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;
using RegReturns.Infrastructure.Auditing;

namespace RegReturns.Infrastructure.Persistence;

/// <summary>
/// EF Core unit of work for the RegReturns database. In the hosts every save also appends an audit entry per changed
/// aggregate to the hash chain, in the same transaction (ADR 0024).
/// </summary>
public sealed class RegReturnsDbContext : DbContext, IAppDbContext
{
    /// <summary>Maximum stored length of an enum name.</summary>
    internal const int EnumMaxLength = 40;

    private readonly IDataChangeAuditor? _auditor;

    /// <summary>
    /// Creates a context that writes no audit entries for its changes: for the migrator and seeding, design time, the
    /// audit writers' own contexts and test helpers. Dependency injection uses it when no auditor is registered.
    /// </summary>
    /// <param name="options">The context options.</param>
    public RegReturnsDbContext(DbContextOptions<RegReturnsDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Creates a context that records every saved data change in the audit chain. Dependency injection uses it in the
    /// hosts, where <c>AddAuditTrail</c> registers the auditor.
    /// </summary>
    /// <param name="options">The context options.</param>
    /// <param name="auditor">Describes the changes to record.</param>
    public RegReturnsDbContext(DbContextOptions<RegReturnsDbContext> options, IDataChangeAuditor auditor)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(auditor);
        _auditor = auditor;
    }

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

    /// <summary>
    /// Gets the audit chain. Append only through <see cref="AuditTrail"/> or <see cref="AuditedSave"/>, which take
    /// the chain lock; never add entries directly.
    /// </summary>
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    /// <inheritdoc />
    IQueryable<AuditEntry> IAppDbContext.AuditEntries => AuditEntries.AsNoTracking();

    /// <summary>Gets a value indicating whether saves record their changes in the audit chain.</summary>
    internal bool AuditsChanges => _auditor is not null;

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var batch = PrepareAudit();
        return batch is null
            ? base.SaveChanges(acceptAllChangesOnSuccess)
            : AuditedSave.Save(this, batch, accept => base.SaveChanges(accept), acceptAllChangesOnSuccess);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var batch = PrepareAudit();
        return batch is null
            ? base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken)
            : AuditedSave.SaveAsync(this, batch, (accept, ct) => base.SaveChangesAsync(accept, ct), acceptAllChangesOnSuccess, cancellationToken);
    }

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

    private AuditBatch? PrepareAudit()
    {
        if (_auditor is null)
        {
            return null;
        }

        if (ChangeTracker.AutoDetectChangesEnabled)
        {
            ChangeTracker.DetectChanges();
        }

        return _auditor.Prepare(ChangeTracker);
    }
}
