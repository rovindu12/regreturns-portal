using System.Data;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Demo;
using RegReturns.Domain.Auditing;
using RegReturns.Domain.Common;
using RegReturns.Domain.Identity;
using RegReturns.Infrastructure.Auditing;
using RegReturns.Infrastructure.Persistence;
using RegReturns.Infrastructure.Persistence.Seeding;

namespace RegReturns.Infrastructure.Demo;

/// <summary>
/// Resets the demo (ADR 0031) in one transaction on a context that audits no data changes: takes the reset lock
/// without waiting, checks the cooldown and that every person is a demo account, deletes the workload tables in
/// foreign-key order, seeds them again for the existing institutions and users, and appends one <c>DemoReset</c>
/// entry under the chain lock before it commits. Institutions, users, API clients and the audit trail are kept.
/// </summary>
/// <param name="contextOptions">Options for a dedicated context without the data-change auditor.</param>
/// <param name="hasher">Seals the audit entry.</param>
/// <param name="timeProvider">The clock.</param>
internal sealed class DemoResetService(
    DbContextOptions<RegReturnsDbContext> contextOptions,
    AuditHasher hasher,
    TimeProvider timeProvider) : IDemoReset
{
    /// <summary>The <c>sp_getapplock</c> resource that keeps two resets apart.</summary>
    public const string LockResource = "RegReturns.DemoReset";

    /// <summary>The tables a reset empties and seeds again, children before parents.</summary>
    public static readonly IReadOnlyList<(string Schema, string Table)> WorkloadTables =
    [
        (Schemas.Returns, "ReturnInsights"),
        (Schemas.Returns, "StoredFiles"),
        (Schemas.Returns, "ValidationFindings"),
        (Schemas.Returns, "SubmissionValues"),
        (Schemas.Returns, "WorkflowEvents"),
        (Schemas.Migration, "RowErrors"),
        (Schemas.Migration, "RunFiles"),
        (Schemas.Migration, "Runs"),
        (Schemas.Returns, "Submissions"),
        (Schemas.Returns, "Obligations"),
        (Schemas.Reference, "ValidationRules"),
        (Schemas.Reference, "TemplateFields"),
        (Schemas.Reference, "TemplateVersions"),
        (Schemas.Reference, "ReturnTypes"),
        (Schemas.Api, "IdempotencyRecords"),
    ];

    /// <summary>The tables a reset keeps: the directory and the audit chain.</summary>
    public static readonly IReadOnlyList<(string Schema, string Table)> KeptTables =
    [
        (Schemas.Reference, "Institutions"),
        (Schemas.Identity, "Users"),
        (Schemas.Identity, "ApiClients"),
        (Schemas.Audit, "AuditEntries"),
    ];

    // Table names come from the constant lists above, never from input.
    private static readonly IReadOnlyList<(string Name, string Sql)> Deletes =
        [.. WorkloadTables.Select(t => ($"{t.Schema}.{t.Table}", $"DELETE FROM [{t.Schema}].[{t.Table}];"))];

    private const string LockSql = $"""
        EXEC @result = sp_getapplock @Resource = N'{LockResource}', @LockMode = N'Exclusive',
            @LockOwner = N'Transaction', @LockTimeout = 0;
        """;

    /// <inheritdoc />
    public async Task<Result<DemoResetReport>> ResetAsync(DemoResetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var db = new RegReturnsDbContext(contextOptions);
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(ct => RunAsync(db, request, ct), cancellationToken);
    }

    private async Task<Result<DemoResetReport>> RunAsync(RegReturnsDbContext db, DemoResetRequest request, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var started = timeProvider.GetTimestamp();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        if (!await TryLockAsync(db, cancellationToken))
        {
            return DemoErrors.InProgress;
        }

        var now = timeProvider.GetUtcNow();
        if (request.Cooldown is { } cooldown)
        {
            var lastReset = await db.AuditEntries.AsNoTracking()
                .Where(e => e.Action == AuditAction.DemoReset)
                .OrderByDescending(e => e.Sequence)
                .Select(e => (DateTimeOffset?)e.OccurredAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (DemoErrors.AvailableAt(lastReset, cooldown) is { } availableAt && availableAt > now)
            {
                return DemoErrors.CoolingDownUntil(availableAt);
            }
        }

        var realPeople = await db.Users.AnyAsync(
            u => !u.IsDemoAccount && u.ApiClientId == null && u.UserName != AppUser.MigrationUserName, cancellationToken);
        if (realPeople)
        {
            return DemoErrors.NotADemoDatabase;
        }

        var removed = new List<DemoTableCount>(Deletes.Count);
        foreach (var (name, sql) in Deletes)
        {
            removed.Add(new DemoTableCount(name, await db.Database.ExecuteSqlRawAsync(sql, cancellationToken)));
        }

        var seeded = await SeedAsync(db, now, cancellationToken);

        var elapsedMs = (long)timeProvider.GetElapsedTime(started).TotalMilliseconds;
        var origin = request.Origin;
        var head = await AuditChain.LockAsync(db, cancellationToken);
        var entry = AuditEntry.Create(
            timeProvider.GetUtcNow(),
            AuditAction.DemoReset,
            origin.Actor.Type,
            origin.Actor.SubjectId,
            origin.Actor.DisplayName,
            origin.Actor.InstitutionCode,
            details: DemoResetReport.Describe(request.Trigger, removed, seeded, elapsedMs),
            ipAddress: origin.IpAddress,
            correlationId: origin.CorrelationId);
        entry.Seal(head.Sequence + 1, head.Hash, hasher.Compute);
        await db.AuditEntries.AddAsync(entry, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new DemoResetReport(request.Trigger, entry.OccurredAt, elapsedMs, entry.Sequence, removed, seeded);
    }

    private static async Task<bool> TryLockAsync(RegReturnsDbContext db, CancellationToken cancellationToken)
    {
        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        await db.Database.ExecuteSqlRawAsync(LockSql, [result], cancellationToken);
        return result.Value is int code && code >= 0;
    }

    private static async Task<DemoSeedCounts> SeedAsync(RegReturnsDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var institutions = await db.Institutions.ToListAsync(cancellationToken);
        var users = await db.Users.Where(u => u.IsDemoAccount).ToListAsync(cancellationToken);
        var data = new DemoDataBuilder(now, new DemoDirectory(institutions, users)).Build();

        var newInstitutions = data.Institutions.Where(i => !institutions.Contains(i)).ToList();
        var newUsers = data.Users.Where(u => !users.Contains(u)).ToList();
        await db.AddRangeAsync(newInstitutions, cancellationToken);
        await db.AddRangeAsync(newUsers, cancellationToken);
        await db.AddRangeAsync(data.ReturnTypes, cancellationToken);
        await db.AddRangeAsync(data.Templates, cancellationToken);
        await db.AddRangeAsync(data.Obligations, cancellationToken);
        await db.AddRangeAsync(data.Submissions, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return new DemoSeedCounts(
            data.ReturnTypes.Count, data.Templates.Count, data.Obligations.Count, data.Submissions.Count, newInstitutions.Count, newUsers.Count);
    }
}
