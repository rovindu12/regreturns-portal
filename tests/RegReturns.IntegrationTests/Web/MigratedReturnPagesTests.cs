using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Time.Testing;

using RegReturns.Domain.Periods;
using RegReturns.Infrastructure.Persistence.Seeding;
using RegReturns.IntegrationTests.Hosting;
using RegReturns.IntegrationTests.Infrastructure;
using RegReturns.IntegrationTests.Migration;

namespace RegReturns.IntegrationTests.Web;

/// <summary>
/// Returns migrated from the legacy system (ADR 0029) show in the portal like any approved return, with a history
/// that says where they came from.
/// </summary>
[Collection(HostedAppsDefinition.Name)]
public sealed class MigratedReturnPagesTests : IClassFixture<MigratedReturnPagesTests.MigratedDatabase>, IDisposable
{
    private static readonly ReportingPeriod October2024 = ReportingPeriod.Monthly(2024, 10);

    private readonly WebApplicationFactory<Program> _factory;
    private readonly MigratedDatabase _database;

    public MigratedReturnPagesTests(MigratedDatabase database)
    {
        _database = database;
        _factory = PortalHost.Create(database.ConnectionString);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Supervisor_sees_a_migrated_return_as_approved_with_its_legacy_history()
    {
        var id = await MigratedReturnIdAsync(DemoBank.Harbourline);
        using var reviewer = await new SupervisionPortal(_factory, _database.ConnectionString).ReviewerAsync();

        var page = await PortalForms.GetTextAsync(reviewer, $"/supervision/returns/{id}");

        page.ShouldContain("Approved");
        page.ShouldContain("legacy migration");
        page.ShouldContain("Migrated from the legacy system, approved");
        page.ShouldContain("Migrated from VRRS (Valoria Returns Reporting System) (VRRS_MLR_EXPORT.csv, line ");
    }

    [Fact]
    public async Task The_bank_sees_its_own_migrated_return()
    {
        var id = await MigratedReturnIdAsync(DemoBank.Harbourline);
        using var maker = await new BankPortal(_factory, _database.ConnectionString).MakerAsync();

        var page = await PortalForms.GetTextAsync(maker, $"/bank/returns/{id}");

        page.ShouldContain("started from legacy migration");
        page.ShouldContain("Migrated from the legacy system, approved");
    }

    private async Task<Guid> MigratedReturnIdAsync(string bank)
    {
        await using var db = SqlServerFixture.CreateContext(_database.ConnectionString);
        return (await LegacyMigrationHarness.MigratedReturnAsync(db, bank, MlrTemplate.Code, October2024, Ct)).Id;
    }

    /// <summary>A seeded database of the class's own, with the sample exports migrated into it once.</summary>
    /// <param name="sql">The shared SQL Server container.</param>
    public sealed class MigratedDatabase(SqlServerFixture sql) : IAsyncLifetime
    {
        /// <summary>Gets the connection string of the database.</summary>
        public string ConnectionString { get; private set; } = string.Empty;

        public async ValueTask InitializeAsync()
        {
            ConnectionString = await LegacyMigrationHarness.CreateSeededDatabaseAsync(sql, CancellationToken.None);
            var source = Directory.CreateTempSubdirectory("regreturns-legacy-");
            try
            {
                LegacyMigrationHarness.WriteSamples(source.FullName);
                var (exitCode, output) = await LegacyMigrationHarness.MigrateAsync(
                    ConnectionString, source.FullName, dryRun: false, new FakeTimeProvider(SqlServerFixture.SeedDate), CancellationToken.None);
                exitCode.ShouldBe(0, output);
            }
            finally
            {
                source.Delete(recursive: true);
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
