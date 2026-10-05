using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using RegReturns.Infrastructure.Persistence;
using RegReturns.IntegrationTests.Infrastructure;

namespace RegReturns.IntegrationTests.Web;

/// <summary>
/// A migrated and seeded database of its own for a test class whose requests write audit entries, so counts are not
/// disturbed by other classes and the shared database stays read-mostly.
/// </summary>
/// <param name="sql">The shared SQL Server container.</param>
public sealed class PortalDatabaseFixture(SqlServerFixture sql) : IAsyncLifetime
{
    /// <summary>Gets the connection string of this fixture's database.</summary>
    public string ConnectionString { get; } = sql.NewDatabaseConnectionString();

    public async ValueTask InitializeAsync()
    {
        await using var context = SqlServerFixture.CreateContext(ConnectionString);
        var initializer = new DatabaseInitializer(context, new FakeTimeProvider(SqlServerFixture.SeedDate), NullLogger<DatabaseInitializer>.Instance);
        await initializer.MigrateAsync(CancellationToken.None);
        await initializer.SeedAsync(CancellationToken.None);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
