using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using RegReturns.Domain.Auditing;
using RegReturns.Domain.Identity;
using RegReturns.Domain.Institutions;
using RegReturns.Infrastructure.Persistence;
using RegReturns.IntegrationTests.Infrastructure;

namespace RegReturns.IntegrationTests.Api;

/// <summary>
/// A freshly migrated and seeded database for API tests, which register their own API clients and read the audit
/// rows their requests write. Seed data has five institutions and no API clients.
/// </summary>
public sealed class ApiDatabase
{
    private ApiDatabase(string connectionString) => ConnectionString = connectionString;

    /// <summary>Gets the connection string.</summary>
    public string ConnectionString { get; }

    /// <summary>Creates, migrates and seeds a new database on the shared SQL Server container.</summary>
    public static async Task<ApiDatabase> CreateAsync(SqlServerFixture sql)
    {
        var database = new ApiDatabase(sql.NewDatabaseConnectionString());
        await using var context = SqlServerFixture.CreateContext(database.ConnectionString);
        var initializer = new DatabaseInitializer(context, new FakeTimeProvider(SqlServerFixture.SeedDate), NullLogger<DatabaseInitializer>.Instance);
        await initializer.MigrateAsync(CancellationToken.None);
        await initializer.SeedAsync(CancellationToken.None);
        return database;
    }

    /// <summary>
    /// Registers a new API client for an institution with the client user it acts through, as IamBootstrap does.
    /// Every call returns a new client id, so tests never share the API's lookup cache or the audit de-duplication
    /// window.
    /// </summary>
    public async Task<string> RegisterClientAsync(
        string institutionCode, bool active, CancellationToken cancellationToken, bool withClientUser = true)
    {
        var clientId = UnregisteredClientId(institutionCode);
        await using var context = SqlServerFixture.CreateContext(ConnectionString);
        var institution = await context.Institutions.SingleAsync(i => i.Code == institutionCode, cancellationToken);
        var client = ApiClient.Create(institution, clientId, $"Test client for {institutionCode}");
        if (!active)
        {
            client.Deactivate();
        }

        await context.ApiClients.AddAsync(client, cancellationToken);
        if (withClientUser)
        {
            await context.Users.AddAsync(AppUser.ForApiClient(client), cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
        return clientId;
    }

    /// <summary>Returns a client id that looks like a bank's but is not in <c>iam.ApiClients</c>.</summary>
    public static string UnregisteredClientId(string institutionCode) =>
        string.Concat("regreturns-bank-", institutionCode, "-", Guid.NewGuid().ToString("N"));

    /// <summary>Returns the audit entries written for one request, identified by its trace id.</summary>
    public async Task<List<AuditEntry>> AuditEntriesAsync(string traceId, CancellationToken cancellationToken)
    {
        await using var context = SqlServerFixture.CreateContext(ConnectionString);
        return await context.AuditEntries.AsNoTracking()
            .Where(e => e.CorrelationId == traceId)
            .ToListAsync(cancellationToken);
    }
}
