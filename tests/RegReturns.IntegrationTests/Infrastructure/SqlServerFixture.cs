using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using RegReturns.Infrastructure.Persistence;

using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(RegReturns.IntegrationTests.Infrastructure.SqlServerFixture))]

namespace RegReturns.IntegrationTests.Infrastructure;

/// <summary>
/// Starts one SQL Server container for the whole test run, applies migrations and loads the demo data once.
/// Tests that change data must use their own database (see <see cref="NewDatabaseConnectionString"/>).
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    /// <summary>The anchor date for demo data, so assertions are stable whatever day the tests run.</summary>
    public static readonly DateTimeOffset SeedDate = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The SQL Server image used by the tests. Keep it in step with the <c>sqlserver</c> service in
    /// <c>docker-compose.yml</c>; Dependabot only bumps the compose file.
    /// </summary>
    public const string Image = "mcr.microsoft.com/mssql/server:2025-latest";

    private readonly MsSqlContainer _container = new MsSqlBuilder(Image).Build();

    /// <summary>Gets the connection string of the seeded shared database.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = DatabaseConnectionString("RegReturns");

        await using var context = CreateContext(ConnectionString);
        var initializer = new DatabaseInitializer(context, new FakeTimeProvider(SeedDate), NullLogger<DatabaseInitializer>.Instance);
        await initializer.MigrateAsync(CancellationToken.None);
        await initializer.SeedAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Returns a connection string for a new, not-yet-created database on the same server.</summary>
    public string NewDatabaseConnectionString() => DatabaseConnectionString($"RegReturns_{Guid.NewGuid():N}");

    public static RegReturnsDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<RegReturnsDbContext>().UseSqlServer(connectionString, sql => sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)).Options);

    private string DatabaseConnectionString(string database) =>
        new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = database }
            .ConnectionString;
}
