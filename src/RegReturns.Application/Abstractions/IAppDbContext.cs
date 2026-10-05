using Microsoft.EntityFrameworkCore;

using RegReturns.Domain.Auditing;
using RegReturns.Domain.Identity;
using RegReturns.Domain.Institutions;
using RegReturns.Domain.Obligations;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;

namespace RegReturns.Application.Abstractions;

/// <summary>
/// The unit of work used by application use cases. Implemented by the EF Core context in Infrastructure.
/// </summary>
public interface IAppDbContext
{
    /// <summary>Gets the institutions.</summary>
    DbSet<Institution> Institutions { get; }

    /// <summary>Gets the user directory projection.</summary>
    DbSet<AppUser> Users { get; }

    /// <summary>Gets the return types.</summary>
    DbSet<ReturnType> ReturnTypes { get; }

    /// <summary>Gets the template versions, including their fields and rules.</summary>
    DbSet<TemplateVersion> TemplateVersions { get; }

    /// <summary>Gets the filing obligations.</summary>
    DbSet<ReturnObligation> Obligations { get; }

    /// <summary>Gets the submissions, including values, findings and workflow events.</summary>
    DbSet<Submission> Submissions { get; }

    /// <summary>Gets the uploaded return files (content included; project the columns you need).</summary>
    DbSet<StoredFile> StoredFiles { get; }

    /// <summary>Gets the banks' machine-to-machine clients registered in WSO2.</summary>
    DbSet<ApiClient> ApiClients { get; }

    /// <summary>
    /// Gets the audit trail, read-only and untracked. Entries are appended only by the audit chain writers: the
    /// <c>IAuditTrail</c> service and the data-change auditing that runs when changes are saved (ADR 0024).
    /// </summary>
    IQueryable<AuditEntry> AuditEntries { get; }

    /// <summary>Saves all changes in one transaction, with an audit entry per changed aggregate when the host audits changes.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The number of rows written.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
