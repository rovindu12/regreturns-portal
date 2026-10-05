using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using RegReturns.Application.Auditing;
using RegReturns.Domain.Auditing;
using RegReturns.Domain.Institutions;
using RegReturns.Infrastructure.Auditing;
using RegReturns.Infrastructure.Persistence;
using RegReturns.IntegrationTests.Infrastructure;

namespace RegReturns.IntegrationTests.Auditing;

/// <summary>
/// Tamper detection (ADR 0016, ADR 0024): with the append-only trigger disabled, as an attacker with database rights
/// could, an edited, deleted or re-ordered entry breaks the chain and the verifier names the first entry affected and
/// how. Batches of two make every chain span several reads.
/// </summary>
public sealed class AuditChainVerifierTests(SqlServerFixture sql)
{
    private const int Entries = 5;

    /// <summary>The entry whose save recorded a data change, with a change document.</summary>
    private const int DataChangeSequence = 4;

    private static readonly AuditHasher Hasher = new(Options.Create(new AuditOptions { HmacKey = TestAuth.AuditKey }));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Empty_trail_is_intact_and_has_no_head()
    {
        var options = await CreateDatabaseAsync();

        var result = await VerifyAsync(options);

        result.ShouldBe(new ChainVerification(0, 0, null, null));
        result.Describe().ShouldBe("The audit chain is intact: 0 entries checked. The audit trail is empty.");
    }

    [Fact]
    public async Task Untouched_chain_is_intact_and_reports_its_head()
    {
        var options = await CreateChainAsync();
        var head = (await LoadAsync(options))[^1];

        var result = await VerifyAsync(options);

        result.ShouldBe(new ChainVerification(Entries, Entries, head.Hash, null));
    }

    [Fact]
    public async Task Edited_entry_is_reported_as_edited()
    {
        var options = await CreateChainAsync();
        await TamperAsync(options, "UPDATE audit.AuditEntries SET ActorDisplayName = N'Someone Else' WHERE Sequence = 3;");

        var result = await VerifyAsync(options);

        result.FirstBreak.ShouldBe(ChainBreak.Edited(3));
        result.EntriesChecked.ShouldBe(3);
    }

    [Fact]
    public async Task Edited_change_document_is_reported_as_edited()
    {
        var options = await CreateChainAsync();
        await TamperAsync(
            options, $"UPDATE audit.AuditEntries SET Changes = REPLACE(Changes, N'Renamed Bank', N'Other Name') WHERE Sequence = {DataChangeSequence};");
        (await LoadAsync(options))[DataChangeSequence - 1].Changes.ShouldNotBeNull().ShouldContain("Other Name");

        var result = await VerifyAsync(options);

        result.FirstBreak.ShouldBe(ChainBreak.Edited(DataChangeSequence));
    }

    [Fact]
    public async Task Deleted_entry_is_reported_as_missing()
    {
        var options = await CreateChainAsync();
        await TamperAsync(options, "DELETE FROM audit.AuditEntries WHERE Sequence = 3;");

        var result = await VerifyAsync(options);

        result.FirstBreak.ShouldBe(ChainBreak.Missing(3, 4));
        result.Describe().ShouldContain("Entry 3 is missing: entry 4 follows entry 2.");
    }

    [Fact]
    public async Task Re_ordered_entries_are_reported_as_a_broken_link()
    {
        var options = await CreateChainAsync();
        await TamperAsync(options, "UPDATE audit.AuditEntries SET Sequence = CASE Sequence WHEN 2 THEN 3 ELSE 2 END WHERE Sequence IN (2, 3);");

        var result = await VerifyAsync(options);

        result.FirstBreak.ShouldBe(ChainBreak.Unlinked(2));
    }

    [Fact]
    public async Task Replaced_first_entry_is_reported_as_a_broken_link()
    {
        var options = await CreateChainAsync();
        await TamperAsync(options, "UPDATE audit.AuditEntries SET PreviousHash = REPLICATE('0', 63) + '1' WHERE Sequence = 1;");

        var result = await VerifyAsync(options);

        result.FirstBreak.ShouldBe(ChainBreak.Unlinked(1));
    }

    [Fact]
    public async Task Entries_deleted_from_the_end_leave_a_shorter_intact_chain_with_an_older_head()
    {
        var options = await CreateChainAsync();
        var before = await VerifyAsync(options);
        await TamperAsync(options, $"DELETE FROM audit.AuditEntries WHERE Sequence = {Entries};");

        var after = await VerifyAsync(options);

        // The limit ADR 0024 records: only the head a previous verification logged reveals the loss.
        after.IsIntact.ShouldBeTrue();
        after.HeadSequence.ShouldBe(before.HeadSequence - 1);
        after.HeadHash.ShouldNotBe(before.HeadHash);
    }

    private static Task<ChainVerification> VerifyAsync(DbContextOptions<RegReturnsDbContext> options) =>
        new AuditChainVerifier(options, Hasher, NullLogger<AuditChainVerifier>.Instance) { BatchSize = 2 }.VerifyAsync(Ct);

    /// <summary>
    /// A chain of <see cref="Entries"/> entries: three recorded events, a data change (entry
    /// <see cref="DataChangeSequence"/>, which renames a bank) and one more event.
    /// </summary>
    private async Task<DbContextOptions<RegReturnsDbContext>> CreateChainAsync()
    {
        var options = await CreateDatabaseAsync();
        var clock = new FakeTimeProvider(SqlServerFixture.SeedDate);
        var trail = new AuditTrail(options, Hasher, clock);
        var origin = new AuditOrigin(new AuditActor(ActorType.User, "subject-auditor", "Avery Auditor", null), "10.1.2.3", null);
        for (var i = 1; i < DataChangeSequence; i++)
        {
            await trail.RecordAsync(origin.ToRecord(AuditAction.AccessDenied, $"attempt={i}"), Ct);
        }

        await using (var db = new RegReturnsDbContext(options, new DataChangeAuditor(new FixedAuditContext(origin), Hasher, clock)))
        {
            await db.Institutions.AddAsync(Institution.Create("RNB", "Renamed Bank", LicenceCategory.Commercial), Ct);
            await db.SaveChangesAsync(Ct);
        }

        await trail.RecordAsync(origin.ToRecord(AuditAction.AccessDenied, "attempt=last"), Ct);
        var entries = await LoadAsync(options);
        entries.Count.ShouldBe(Entries);
        entries[DataChangeSequence - 1].Changes.ShouldNotBeNull().ShouldContain("Renamed Bank");
        return options;
    }

    private static async Task<List<AuditEntry>> LoadAsync(DbContextOptions<RegReturnsDbContext> options)
    {
        await using var db = new RegReturnsDbContext(options);
        return await db.AuditEntries.AsNoTracking().OrderBy(e => e.Sequence).ToListAsync(Ct);
    }

    private static async Task TamperAsync(DbContextOptions<RegReturnsDbContext> options, string tamperSql)
    {
        await using var db = new RegReturnsDbContext(options);
        await db.Database.ExecuteSqlRawAsync("DISABLE TRIGGER audit.TR_AuditEntries_AppendOnly ON audit.AuditEntries;", Ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync(tamperSql, Ct);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("ENABLE TRIGGER audit.TR_AuditEntries_AppendOnly ON audit.AuditEntries;", CancellationToken.None);
        }
    }

    private async Task<DbContextOptions<RegReturnsDbContext>> CreateDatabaseAsync()
    {
        var options = new DbContextOptionsBuilder<RegReturnsDbContext>().UseSqlServer(sql.NewDatabaseConnectionString()).Options;
        await using var db = new RegReturnsDbContext(options);
        await new DatabaseInitializer(db, new FakeTimeProvider(SqlServerFixture.SeedDate), NullLogger<DatabaseInitializer>.Instance)
            .MigrateAsync(Ct);
        return options;
    }

    private sealed class FixedAuditContext(AuditOrigin origin) : IAuditContext
    {
        public AuditOrigin Current => origin;
    }
}
