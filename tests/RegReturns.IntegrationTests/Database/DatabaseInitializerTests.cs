using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using RegReturns.Domain.Submissions;
using RegReturns.Infrastructure.Persistence;
using RegReturns.IntegrationTests.Infrastructure;

namespace RegReturns.IntegrationTests.Database;

public sealed class DatabaseInitializerTests(SqlServerFixture sql)
{
    [Fact]
    public async Task Migrate_and_seed_create_a_complete_demo_database_and_are_idempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var context = SqlServerFixture.CreateContext(sql.NewDatabaseConnectionString());
        var initializer = new DatabaseInitializer(context, new FakeTimeProvider(SqlServerFixture.SeedDate), NullLogger<DatabaseInitializer>.Instance);

        (await initializer.MigrateAsync(ct)).ShouldNotBeEmpty();
        (await initializer.SeedAsync(ct)).ShouldBeTrue();

        (await initializer.MigrateAsync(ct)).ShouldBeEmpty();
        (await initializer.SeedAsync(ct)).ShouldBeFalse();
        (await context.Institutions.CountAsync(ct)).ShouldBe(5);
    }

    [Fact]
    public async Task Seeded_submission_round_trips_with_its_values_findings_and_history()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var context = SqlServerFixture.CreateContext(sql.ConnectionString);

        var returned = await context.Submissions
            .Include(s => s.Values)
            .Include(s => s.Findings)
            .Include(s => s.Events)
            .SingleAsync(s => s.Revision == 2, ct);

        returned.Status.ShouldBe(SubmissionStatus.Approved);
        returned.Values.Count.ShouldBe(11);
        returned.Findings.ShouldContain(f => f.Revision == 1 && f.Justification != null);
        returned.Events.Select(e => e.Action).ShouldContain(WorkflowAction.ReturnForCorrection);
    }

    [Fact]
    public async Task Database_enforces_one_obligation_per_bank_return_and_period()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var context = SqlServerFixture.CreateContext(sql.ConnectionString);
        var existing = await context.Obligations.AsNoTracking().FirstAsync(ct);

        var duplicate = await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            BEGIN TRAN;
            BEGIN TRY
                INSERT INTO returns.Obligations (Id, InstitutionId, ReturnTypeId, DueDate, Status, IsLate, PeriodFrequency, PeriodYear, PeriodNumber)
                VALUES (NEWID(), {existing.InstitutionId}, {existing.ReturnTypeId}, {existing.DueDate}, 'Open', 0,
                        {existing.Period.Frequency.ToString()}, {existing.Period.Year}, {existing.Period.Number});
                ROLLBACK;
                SELECT 0;
            END TRY
            BEGIN CATCH
                ROLLBACK;
                THROW;
            END CATCH
            """, ct).ShouldThrowAsync<Microsoft.Data.SqlClient.SqlException>();

        duplicate.Message.ShouldContain("UX_Obligations_Institution_Return_Period");
    }

    [Fact]
    public async Task Concurrent_edits_to_the_same_submission_are_detected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var context = SqlServerFixture.CreateContext(sql.NewDatabaseConnectionString());
        var initializer = new DatabaseInitializer(context, new FakeTimeProvider(SqlServerFixture.SeedDate), NullLogger<DatabaseInitializer>.Instance);
        await initializer.MigrateAsync(ct);
        await initializer.SeedAsync(ct);
        var connectionString = context.Database.GetConnectionString()!;

        await using var first = SqlServerFixture.CreateContext(connectionString);
        await using var second = SqlServerFixture.CreateContext(connectionString);
        var id = await first.Submissions.Select(s => s.Id).FirstAsync(ct);
        var a = await first.Submissions.SingleAsync(s => s.Id == id, ct);
        var b = await second.Submissions.SingleAsync(s => s.Id == id, ct);

        first.Entry(a).Property(nameof(Submission.IsLate)).CurrentValue = !a.IsLate;
        await first.SaveChangesAsync(ct);
        second.Entry(b).Property(nameof(Submission.IsLate)).CurrentValue = !b.IsLate;

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(ct));
    }
}
