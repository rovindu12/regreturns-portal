using System.Diagnostics.CodeAnalysis;

using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Identity;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Common;
using RegReturns.Domain.Identity;

namespace RegReturns.Application.Diagnostics;

/// <summary>What the database says about itself, for the diagnostics page (ADR 0033).</summary>
/// <param name="Database">The database name.</param>
/// <param name="ServerVersion">SQL Server's product version and edition.</param>
/// <param name="Encryption">How this connection is encrypted, as SQL Server reports it, or <see langword="null"/> when the login may not see it.</param>
/// <param name="ClientEncryption">How the app asks for encryption and checks the certificate, from its connection settings.</param>
/// <param name="AppliedMigrations">The number of applied EF Core migrations.</param>
/// <param name="LatestMigration">The latest applied migration.</param>
/// <param name="PendingMigrations">Migrations in the build that the database lacks.</param>
public sealed record DatabaseDiagnostics(
    string Database,
    string ServerVersion,
    string? Encryption,
    string ClientEncryption,
    int AppliedMigrations,
    string? LatestMigration,
    IReadOnlyList<string> PendingMigrations);

/// <summary>Reads <see cref="DatabaseDiagnostics"/>; implemented over the EF Core context in Infrastructure.</summary>
public interface IDatabaseDiagnostics
{
    /// <summary>Reads the database's facts.</summary>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The facts.</returns>
    Task<DatabaseDiagnostics> GetAsync(CancellationToken cancellationToken);
}

/// <summary>Asks for the diagnostics an administrator troubleshoots with (ADR 0033).</summary>
[SuppressMessage(
    "Major Code Smell",
    "S2094:Classes should not be empty",
    Justification = "A query without inputs: the handler interface needs a type to dispatch on.")]
public sealed record GetDiagnostics;

/// <summary>The newest audit entry: the head of the hash chain.</summary>
/// <param name="Sequence">Its sequence number.</param>
/// <param name="OccurredAt">When it was written.</param>
public sealed record AuditChainHead(long Sequence, DateTimeOffset OccurredAt);

/// <summary>The data side of the diagnostics page.</summary>
/// <param name="Database">The database's facts.</param>
/// <param name="AuditHead">The head of the audit chain, if it has entries.</param>
/// <param name="ActiveUsers">People who may sign in.</param>
/// <param name="DisabledUsers">People whose access is disabled.</param>
public sealed record DiagnosticsReport(DatabaseDiagnostics Database, AuditChainHead? AuditHead, int ActiveUsers, int DisabledUsers);

/// <summary>Reads the diagnostics for a system administrator. Holds no figures and no personal data.</summary>
/// <param name="db">The unit of work.</param>
/// <param name="database">The database's own facts.</param>
/// <param name="currentActor">The caller.</param>
public sealed class GetDiagnosticsHandler(IAppDbContext db, IDatabaseDiagnostics database, ICurrentActor currentActor)
    : IQueryHandler<GetDiagnostics, Result<DiagnosticsReport>>
{
    /// <inheritdoc />
    public async Task<Result<DiagnosticsReport>> HandleAsync(GetDiagnostics query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var actor = await currentActor.GetAsync(cancellationToken);
        if (actor.IsFailure || !actor.Value.HasRole(Role.SystemAdmin))
        {
            return UserAdministrationErrors.AdminsOnly;
        }

        var facts = await database.GetAsync(cancellationToken);
        var head = await db.AuditEntries.AsNoTracking()
            .OrderByDescending(e => e.Sequence)
            .Select(e => new AuditChainHead(e.Sequence, e.OccurredAt))
            .FirstOrDefaultAsync(cancellationToken);
        var people = db.Users.AsNoTracking().Where(u => u.ApiClientId == null && u.UserName != AppUser.MigrationUserName);
        var active = await people.CountAsync(u => u.Status == UserStatus.Active, cancellationToken);
        var disabled = await people.CountAsync(u => u.Status == UserStatus.Disabled, cancellationToken);
        return new DiagnosticsReport(facts, head, active, disabled);
    }
}

/// <summary>The database facts of a host that cannot read them: every field says so.</summary>
public sealed class UnavailableDatabaseDiagnostics : IDatabaseDiagnostics
{
    /// <inheritdoc />
    public Task<DatabaseDiagnostics> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new DatabaseDiagnostics("unknown", "unknown", null, "unknown", 0, null, []));
}
