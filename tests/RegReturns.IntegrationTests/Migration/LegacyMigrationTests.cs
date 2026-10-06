extern alias MigratorTool;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using MigratorTool::RegReturns.Migrator.Commands;
using MigratorTool::RegReturns.Migrator.Legacy;

using RegReturns.Application.Auditing;
using RegReturns.Application.Migration;
using RegReturns.Domain.Auditing;
using RegReturns.Domain.Identity;
using RegReturns.Domain.Migration;
using RegReturns.Domain.Obligations;
using RegReturns.Domain.Periods;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;
using RegReturns.Infrastructure.Auditing;
using RegReturns.Infrastructure.Legacy;
using RegReturns.Infrastructure.Persistence;
using RegReturns.Infrastructure.Persistence.Seeding;
using RegReturns.IntegrationTests.Infrastructure;

namespace RegReturns.IntegrationTests.Migration;

/// <summary>
/// Runs the legacy migration (ADR 0029) end to end against a seeded database with the committed sample exports, the way
/// <c>regreturns-migrator legacy</c> does: audited saves, one transaction, reconciliation before commit, and the exit
/// codes the tool returns. Each test uses a database of its own.
/// </summary>
public sealed class LegacyMigrationTests(SqlServerFixture sql) : IDisposable
{
    /// <summary>Rows read, blank, superseded, rejected and migrated over the three sample files.</summary>
    private static readonly MigrationTotals SampleTotals = new(255, 2, 3, 13, 237, 0);

    private const int MigratedReturns = LegacyMigrationHarness.SampleReturns;

    private readonly FakeTimeProvider _clock = new(SqlServerFixture.SeedDate);

    private readonly DirectoryInfo _source = Directory.CreateTempSubdirectory("regreturns-legacy-");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _source.Delete(recursive: true);

    [Fact]
    public async Task Dry_run_reconciles_and_keeps_only_the_run_and_its_row_errors()
    {
        var database = await CreateDatabaseAsync();
        WriteSamples();

        var (exitCode, output) = await MigrateAsync(database, dryRun: true);

        exitCode.ShouldBe(LegacyCommands.Reconciled);
        output.ShouldContain("Dry run, so everything was rolled back.");
        await using var db = SqlServerFixture.CreateContext(database);
        (await db.Submissions.CountAsync(s => s.Source == SubmissionSource.Migration, Ct)).ShouldBe(0);
        (await db.Users.AnyAsync(u => u.UserName == AppUser.MigrationUserName, Ct)).ShouldBeFalse();
        var run = await db.MigrationRuns.Include(r => r.Files).Include(r => r.Errors).SingleAsync(Ct);
        (run.IsDryRun, run.Outcome).ShouldBe((true, MigrationOutcome.Reconciled));
        Totals(run).ShouldBe(SampleTotals);
        run.Files.Select(f => f.ReturnTypeCode).ShouldBe([MlrTemplate.Code, MdaTemplate.Code, QcarTemplate.Code], ignoreOrder: true);
        run.Errors.Select(e => e.Code).ShouldContain(MigrationErrors.Superseded);
        run.Errors.Count(e => e.Kind == RowErrorKind.Rejected).ShouldBeGreaterThanOrEqualTo(SampleTotals.RejectedRows);
    }

    [Fact]
    public async Task Dry_run_records_only_the_run_in_the_audit_trail()
    {
        var database = await CreateDatabaseAsync();
        WriteSamples();

        await MigrateAsync(database, dryRun: true);

        var entry = (await AuditEntriesAsync(database)).ShouldHaveSingleItem();
        (entry.Action, entry.EntityType).ShouldBe((AuditAction.Created, nameof(MigrationRun)));
    }

    [Fact]
    public async Task Live_run_files_approved_migrated_returns_through_the_migration_account()
    {
        var database = await CreateDatabaseAsync();
        WriteSamples();

        var (exitCode, output) = await MigrateAsync(database, dryRun: false);

        exitCode.ShouldBe(LegacyCommands.Reconciled);
        output.ShouldContain("Reconciled and committed: 237 return(s) migrated");
        await using var db = SqlServerFixture.CreateContext(database);
        var account = await db.Users.SingleAsync(u => u.UserName == AppUser.MigrationUserName, Ct);
        var migrated = await db.Submissions.Where(s => s.Source == SubmissionSource.Migration).ToListAsync(Ct);
        migrated.Count.ShouldBe(MigratedReturns);
        migrated.ShouldAllBe(s => s.Status == SubmissionStatus.Approved && s.PreparedByUserId == account.Id && s.DecidedByUserId == account.Id);
        var obligationIds = migrated.Select(s => s.ObligationId).ToList();
        (await db.Obligations.CountAsync(o => obligationIds.Contains(o.Id) && o.Status == ObligationStatus.Fulfilled, Ct))
            .ShouldBe(MigratedReturns);
        var run = await db.MigrationRuns.SingleAsync(Ct);
        (run.IsDryRun, run.Outcome, run.MigratedReturns).ShouldBe((false, MigrationOutcome.Reconciled, MigratedReturns));
    }

    [Fact]
    public async Task A_migrated_return_keeps_the_legacy_dates_its_warnings_and_where_it_came_from()
    {
        var database = await CreateDatabaseAsync();
        WriteSamples();

        await MigrateAsync(database, dryRun: false);

        // Northgate filed its October 2024 liquidity return three days late in the legacy system.
        await using var db = SqlServerFixture.CreateContext(database);
        var late = await MigratedReturnAsync(db, DemoBank.Northgate, MlrTemplate.Code, ReportingPeriod.Monthly(2024, 10));
        late.IsLate.ShouldBeTrue();
        late.FirstSubmittedAt.ShouldNotBeNull().ShouldBeLessThan(late.DecidedAt.ShouldNotBeNull());
        late.DecidedAt.Value.ShouldBeLessThan(SqlServerFixture.SeedDate);
        var migrateEvent = late.Events.ShouldHaveSingleItem();
        migrateEvent.Action.ShouldBe(WorkflowAction.Migrate);
        migrateEvent.Comment.ShouldNotBeNull().ShouldStartWith("Migrated from VRRS (Valoria Returns Reporting System) (VRRS_MLR_EXPORT.csv, line ");
        (await MigratedReturnAsync(db, DemoBank.Harbourline, MlrTemplate.Code, ReportingPeriod.Monthly(2024, 1))).IsLate.ShouldBeFalse();

        // Lotus Union's unusual HQLA mix in August 2024 raised warnings that VRRS accepted without a justification.
        var shock = await MigratedReturnAsync(db, DemoBank.LotusUnion, MlrTemplate.Code, ReportingPeriod.Monthly(2024, 8));
        shock.Findings.ShouldNotBeEmpty();
        shock.Findings.ShouldAllBe(f => f.Severity == Severity.Warning && f.Justification == LegacyMigrator.WarningNote);
    }

    [Fact]
    public async Task Live_run_records_every_migrated_return_in_an_intact_audit_chain()
    {
        var database = await CreateDatabaseAsync();
        WriteSamples();

        await MigrateAsync(database, dryRun: false);

        var entries = await AuditEntriesAsync(database);
        entries.Count(e => e.EntityType == nameof(Submission) && e.Action == AuditAction.Created).ShouldBe(MigratedReturns);
        entries.ShouldAllBe(e => e.ActorType == ActorType.System && e.ActorSubjectId == MigratorAuditContext.Subject);
        var options = new DbContextOptionsBuilder<RegReturnsDbContext>().UseSqlServer(database).Options;
        var verification = await new AuditChainVerifier(options, Hasher, NullLogger<AuditChainVerifier>.Instance).VerifyAsync(Ct);
        verification.EntriesChecked.ShouldBe(entries.Count);
        verification.IsIntact.ShouldBeTrue();
    }

    [Fact]
    public async Task Running_again_finds_every_return_already_migrated_and_still_reconciles()
    {
        var database = await CreateDatabaseAsync();
        WriteSamples();
        await MigrateAsync(database, dryRun: false);

        var (exitCode, _) = await MigrateAsync(database, dryRun: false);

        exitCode.ShouldBe(LegacyCommands.Reconciled);
        await using var db = SqlServerFixture.CreateContext(database);
        (await db.Submissions.CountAsync(s => s.Source == SubmissionSource.Migration, Ct)).ShouldBe(MigratedReturns);
        var rerun = await db.MigrationRuns.OrderByDescending(r => r.StartedAt).FirstAsync(Ct);
        Totals(rerun).ShouldBe(SampleTotals with { MigratedReturns = 0, AlreadyMigratedReturns = MigratedReturns });
        rerun.Outcome.ShouldBe(MigrationOutcome.Reconciled);
    }

    [Fact]
    public async Task A_stored_figure_that_no_longer_matches_the_source_stops_the_whole_run()
    {
        var database = await CreateDatabaseAsync();
        WriteSamples(withoutLastMdaRow: true);
        await MigrateAsync(database, dryRun: false);
        await ChangeStoredFigureAsync(database, DemoBank.Harbourline, MlrTemplate.Code, ReportingPeriod.Monthly(2025, 9), MlrTemplate.TotalDeposits);
        WriteSamples();

        var (exitCode, output) = await MigrateAsync(database, dryRun: false);

        exitCode.ShouldBe(LegacyCommands.Mismatch);
        output.ShouldContain("Nothing was committed");
        await using var db = SqlServerFixture.CreateContext(database);
        var run = await db.MigrationRuns.OrderByDescending(r => r.StartedAt).FirstAsync(Ct);
        (run.Outcome, run.Mismatches).ShouldBe((MigrationOutcome.Mismatch, 1));

        // Meridian's September 2025 MDA return, the one return new in this run, was rolled back with everything else.
        (await db.Submissions.CountAsync(s => s.Source == SubmissionSource.Migration, Ct)).ShouldBe(MigratedReturns - 1);
    }

    [Fact]
    public async Task A_period_already_filed_in_the_portal_is_rejected_and_the_rest_is_migrated()
    {
        var database = await CreateDatabaseAsync();
        await CreatePortalDraftAsync(database, DemoBank.Harbourline, MlrTemplate.Code, ReportingPeriod.Monthly(2025, 9));
        WriteSamples();

        var (exitCode, _) = await MigrateAsync(database, dryRun: false);

        exitCode.ShouldBe(LegacyCommands.Reconciled);
        await using var db = SqlServerFixture.CreateContext(database);
        var run = await db.MigrationRuns.Include(r => r.Errors).SingleAsync(Ct);
        var refusal = run.Errors.Where(e => e.Code == MigrationErrors.AlreadyFiled).ShouldHaveSingleItem();
        (refusal.FileName, refusal.Kind).ShouldBe(("VRRS_MLR_EXPORT.csv", RowErrorKind.Rejected));
        (run.MigratedReturns, run.RejectedRows).ShouldBe((MigratedReturns - 1, SampleTotals.RejectedRows + 1));
        (await db.Submissions.CountAsync(s => s.Source == SubmissionSource.Migration, Ct)).ShouldBe(MigratedReturns - 1);
    }

    [Fact]
    public async Task The_report_folder_gets_the_summary_row_errors_and_reconciliation_files()
    {
        var database = await CreateDatabaseAsync();
        WriteSamples();
        var reports = Path.Combine(_source.FullName, "reports");

        await MigrateAsync(database, dryRun: true, reports);

        Directory.EnumerateFiles(reports).Select(Path.GetFileName).ShouldBe(
            ["reconciliation-by-bank.csv", "reconciliation-by-period.csv", "reconciliation-detail.csv", "row-errors.csv", "summary.csv"],
            ignoreOrder: true);
        var rowErrors = await File.ReadAllLinesAsync(Path.Combine(reports, "row-errors.csv"), Ct);
        rowErrors.ShouldContain(l => l.Contains(MigrationErrors.UnknownInstitution, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_source_file_missing_from_the_mapping_stops_the_run_before_it_starts()
    {
        var database = await CreateDatabaseAsync();
        WriteSamples();
        await File.WriteAllTextAsync(Path.Combine(_source.FullName, "stray.csv"), "a,b\n1,2\n", Ct);

        var (exitCode, output) = await MigrateAsync(database, dryRun: false);

        exitCode.ShouldBe(LegacyCommands.Failed);
        output.ShouldContain(MigrationErrors.SourceMismatch.Code);
        await using var db = SqlServerFixture.CreateContext(database);
        (await db.MigrationRuns.AnyAsync(Ct)).ShouldBeFalse();
    }

    private static readonly AuditHasher Hasher = new(Options.Create(new AuditOptions { HmacKey = TestAuth.AuditKey }));

    private static MigrationTotals Totals(MigrationRun run) => new(
        run.RowsRead, run.BlankRows, run.SupersededRows, run.RejectedRows, run.MigratedReturns, run.AlreadyMigratedReturns);

    private Task<string> CreateDatabaseAsync() => LegacyMigrationHarness.CreateSeededDatabaseAsync(sql, Ct);

    private void WriteSamples(bool withoutLastMdaRow = false) => LegacyMigrationHarness.WriteSamples(_source.FullName, withoutLastMdaRow);

    private async Task<(int ExitCode, string Output)> MigrateAsync(string connectionString, bool dryRun, string? reports = null)
    {
        var result = await LegacyMigrationHarness.MigrateAsync(connectionString, _source.FullName, dryRun, _clock, Ct, reports);
        _clock.Advance(TimeSpan.FromMinutes(1)); // so runs sort by start time
        return result;
    }

    private static async Task<List<AuditEntry>> AuditEntriesAsync(string connectionString)
    {
        await using var db = SqlServerFixture.CreateContext(connectionString);
        return await db.AuditEntries.AsNoTracking().OrderBy(e => e.Sequence).ToListAsync(Ct);
    }

    private static Task<Submission> MigratedReturnAsync(RegReturnsDbContext db, string bank, string returnType, ReportingPeriod period) =>
        LegacyMigrationHarness.MigratedReturnAsync(db, bank, returnType, period, Ct);

    /// <summary>Changes one stored value of a migrated return behind the application's back.</summary>
    private static async Task ChangeStoredFigureAsync(string connectionString, string bank, string returnType, ReportingPeriod period, string fieldCode)
    {
        await using var db = SqlServerFixture.CreateContext(connectionString);
        var submission = await MigratedReturnAsync(db, bank, returnType, period);
        var changed = await db.Database.ExecuteSqlAsync(
            $"UPDATE returns.SubmissionValues SET NumericValue = NumericValue + 1 WHERE SubmissionId = {submission.Id} AND FieldCode = {fieldCode};",
            Ct);
        changed.ShouldBe(1);
    }

    /// <summary>Starts a portal draft for a period the legacy exports also cover.</summary>
    private async Task CreatePortalDraftAsync(string connectionString, string bank, string returnType, ReportingPeriod period)
    {
        await using var db = SqlServerFixture.CreateContext(connectionString);
        var institution = await db.Institutions.SingleAsync(i => i.Code == bank, Ct);
        var type = await db.ReturnTypes.SingleAsync(t => t.Code == returnType, Ct);
        var templates = await db.TemplateVersions.Include(v => v.Fields).Where(v => v.ReturnTypeId == type.Id).ToListAsync(Ct);
        var maker = await db.Users.SingleAsync(u => u.UserName == DemoUsers.MakerUserName(bank), Ct);
        var obligation = ReturnObligation.Create(institution.Id, type, period);
        var draft = Submission.CreateDraft(
            obligation, TemplateVersion.SelectFor(templates, period).ShouldNotBeNull(), maker.ToActor(), SubmissionSource.Web, _clock.GetUtcNow());
        await db.Obligations.AddAsync(obligation, Ct);
        await db.Submissions.AddAsync(draft.Value, Ct);
        await db.SaveChangesAsync(Ct);
    }
}
