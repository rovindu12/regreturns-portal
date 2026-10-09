extern alias MigratorTool;

using System.Text;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using MigratorTool::RegReturns.Migrator.Auditing;
using MigratorTool::RegReturns.Migrator.Commands;
using MigratorTool::RegReturns.Migrator.Legacy;

using RegReturns.Application.Auditing;
using RegReturns.Application.Migration;
using RegReturns.Domain.Periods;
using RegReturns.Domain.Submissions;
using RegReturns.Infrastructure;
using RegReturns.Infrastructure.Legacy;
using RegReturns.Infrastructure.Persistence;
using RegReturns.Infrastructure.Persistence.Seeding;
using RegReturns.IntegrationTests.Infrastructure;

namespace RegReturns.IntegrationTests.Migration;

/// <summary>
/// Runs the legacy migration (ADR 0029) the way <c>regreturns-migrator legacy</c> does, against a database of the
/// test's own: audited saves as the migrator, then the console report and the exit code.
/// </summary>
internal static class LegacyMigrationHarness
{
    /// <summary>Returns migrated from the sample exports into the seeded demo database.</summary>
    public const int SampleReturns = 237;

    /// <summary>Creates, migrates and seeds a new database.</summary>
    /// <param name="sql">The SQL Server fixture.</param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <returns>The database's connection string.</returns>
    public static async Task<string> CreateSeededDatabaseAsync(SqlServerFixture sql, CancellationToken cancellationToken)
    {
        var connectionString = sql.NewDatabaseConnectionString();
        await using var db = SqlServerFixture.CreateContext(connectionString);
        var initializer = new DatabaseInitializer(db, new FakeTimeProvider(SqlServerFixture.SeedDate), NullLogger<DatabaseInitializer>.Instance);
        await initializer.MigrateAsync(cancellationToken);
        await initializer.SeedAsync(cancellationToken);
        return connectionString;
    }

    /// <summary>Writes the sample exports and mapping, optionally without the last row of the MDA file.</summary>
    /// <param name="folder">The source folder.</param>
    /// <param name="withoutLastMdaRow">Whether to leave out Meridian's September 2025 MDA return.</param>
    public static void WriteSamples(string folder, bool withoutLastMdaRow = false)
    {
        foreach (var (name, content) in LegacySamples.Generate())
        {
            var bytes = withoutLastMdaRow && name == LegacySampleGenerator.MdaFile ? WithoutLastRow(content) : content;
            File.WriteAllBytes(Path.Combine(folder, name), bytes);
        }
    }

    /// <summary>Runs the migration with the tool's services and returns its exit code and console output.</summary>
    /// <param name="connectionString">The database.</param>
    /// <param name="folder">The source folder, holding the mapping.</param>
    /// <param name="dryRun">Whether to roll back at the end.</param>
    /// <param name="clock">The clock the migration reads.</param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <param name="reports">Where to write the CSV reports, if anywhere.</param>
    /// <returns>The exit code and what the tool printed.</returns>
    public static async Task<(int ExitCode, string Output)> MigrateAsync(
        string connectionString, string folder, bool dryRun, TimeProvider clock, CancellationToken cancellationToken, string? reports = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:RegReturns"] = connectionString,
                ["Audit:HmacKey"] = TestAuth.AuditKey,
            })
            .Build();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(clock)
            .AddInfrastructure(configuration)
            .AddAuditTrail(configuration)
            .AddSingleton<IAuditContext>(MigratorAuditContext.LegacyMigration)
            .AddLegacyMigration();
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        await using var output = new StringWriter();
        var request = new LegacyMigrationRequest(folder, Path.Combine(folder, LegacySampleGenerator.MappingFile), dryRun);

        var exitCode = await LegacyCommands.MigrateAsync(scope.ServiceProvider, request, reports, output, cancellationToken);
        return (exitCode, output.ToString());
    }

    /// <summary>Loads the migrated return of a bank, return type and period, with its events and findings.</summary>
    /// <param name="db">The context.</param>
    /// <param name="bank">The bank code.</param>
    /// <param name="returnType">The return type code.</param>
    /// <param name="period">The period.</param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <returns>The return.</returns>
    public static async Task<Submission> MigratedReturnAsync(
        RegReturnsDbContext db, string bank, string returnType, ReportingPeriod period, CancellationToken cancellationToken)
    {
        var institution = await db.Institutions.SingleAsync(i => i.Code == bank, cancellationToken);
        var type = await db.ReturnTypes.SingleAsync(t => t.Code == returnType, cancellationToken);
        var obligation = await db.Obligations.SingleAsync(
            o => o.InstitutionId == institution.Id && o.ReturnTypeId == type.Id
                && o.Period.Frequency == period.Frequency && o.Period.Year == period.Year && o.Period.Number == period.Number,
            cancellationToken);
        return await db.Submissions.Include(s => s.Events).Include(s => s.Findings)
            .SingleAsync(s => s.ObligationId == obligation.Id && s.Source == SubmissionSource.Migration, cancellationToken);
    }

    private static byte[] WithoutLastRow(byte[] content)
    {
        var text = Encoding.UTF8.GetString(content).TrimEnd('\r', '\n');
        return Encoding.UTF8.GetBytes(text[..text.LastIndexOf("\r\n", StringComparison.Ordinal)] + "\r\n");
    }
}
