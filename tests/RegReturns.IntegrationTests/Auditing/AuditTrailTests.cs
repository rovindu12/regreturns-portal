using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using RegReturns.Application.Auditing;
using RegReturns.Domain.Auditing;
using RegReturns.Infrastructure.Auditing;
using RegReturns.Infrastructure.Persistence;
using RegReturns.IntegrationTests.Infrastructure;

namespace RegReturns.IntegrationTests.Auditing;

public sealed class AuditTrailTests(SqlServerFixture sql)
{
    private const int AppendOnlyViolation = 51001;

    private static readonly AuditHasher Hasher = new(Options.Create(new AuditOptions { HmacKey = TestAuth.AuditKey }));

    [Fact]
    public async Task Sequential_appends_form_a_valid_chain()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = await CreateDatabaseAsync(ct);
        var clock = new FakeTimeProvider(SqlServerFixture.SeedDate);
        var trail = new AuditTrail(options, Hasher, clock);

        var sequences = new List<long>();
        for (var i = 1; i <= 5; i++)
        {
            sequences.Add(await trail.RecordAsync(Record(i), ct));
            clock.Advance(TimeSpan.FromSeconds(1.2345678));
        }

        sequences.ShouldBe([1, 2, 3, 4, 5]);
        var entries = await LoadAsync(options, ct);
        entries.Select(e => e.Sequence).ShouldBe([1, 2, 3, 4, 5]);
        BrokenLinks(entries).ShouldBeEmpty();
        entries.Where(e => !Hasher.Verify(e)).ShouldBeEmpty();
    }

    [Fact]
    public async Task First_entry_links_to_the_genesis_hash()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = await CreateDatabaseAsync(ct);

        await new AuditTrail(options, Hasher, TimeProvider.System).RecordAsync(Record(1), ct);

        (await LoadAsync(options, ct)).ShouldHaveSingleItem().PreviousHash.ShouldBe(AuditEntry.GenesisHash);
    }

    [Fact]
    public async Task Stored_entry_keeps_every_field_and_the_time_to_the_tick()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = await CreateDatabaseAsync(ct);
        var at = new DateTimeOffset(2026, 10, 4, 9, 30, 15, TimeSpan.Zero).AddTicks(1234567);
        var record = new AuditRecord(
            AuditAction.AccessDenied, ActorType.User, "3f2b9c1e", "Ána Pérez-Ōtsuka", "ALPHA", "Submission", "42",
            "path=/submissions/42; reason=Bank.SubmitReturn", "2001:db8::1", "4bf92f3577b34da6a3ce929d0e0e4736");

        await new AuditTrail(options, Hasher, new FakeTimeProvider(at)).RecordAsync(record, ct);

        var entry = (await LoadAsync(options, ct)).ShouldHaveSingleItem();
        entry.OccurredAt.ShouldBe(at);
        new AuditRecord(
            entry.Action, entry.ActorType, entry.ActorSubjectId, entry.ActorDisplayName, entry.InstitutionCode,
            entry.EntityType, entry.EntityId, entry.Details, entry.IpAddress, entry.CorrelationId).ShouldBe(record);
        Hasher.Verify(entry).ShouldBeTrue();
    }

    [Fact]
    public async Task Concurrent_appends_from_separate_instances_have_no_gaps_and_a_valid_chain()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = await CreateDatabaseAsync(ct, withRetries: true);
        const int writers = 20;
        var trails = Enumerable.Range(0, writers).Select(_ => new AuditTrail(options, Hasher, TimeProvider.System)).ToList();
        using var start = new SemaphoreSlim(0);

        var appends = trails.Select((trail, i) => Task.Run(
            async () =>
            {
                await start.WaitAsync(ct);
                return await trail.RecordAsync(Record(i), ct);
            },
            ct)).ToList();
        start.Release(writers);
        var sequences = await Task.WhenAll(appends);

        sequences.Order().ShouldBe(Enumerable.Range(1, writers).Select(i => (long)i));
        var entries = await LoadAsync(options, ct);
        entries.Select(e => e.Sequence).ShouldBe(Enumerable.Range(1, writers).Select(i => (long)i));
        BrokenLinks(entries).ShouldBeEmpty();
        entries.Where(e => !Hasher.Verify(e)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Database_refuses_to_update_an_entry()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = await CreateDatabaseAsync(ct);
        await new AuditTrail(options, Hasher, TimeProvider.System).RecordAsync(Record(1), ct);
        await using var db = new RegReturnsDbContext(options);

        var refused = await Should.ThrowAsync<SqlException>(() =>
            db.Database.ExecuteSqlRawAsync("UPDATE audit.AuditEntries SET Details = N'rewritten' WHERE Sequence = 1;", ct));

        refused.Number.ShouldBe(AppendOnlyViolation);
        (await LoadAsync(options, ct)).ShouldHaveSingleItem().Details.ShouldBe(Record(1).Details);
    }

    [Fact]
    public async Task Database_refuses_to_delete_an_entry()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = await CreateDatabaseAsync(ct);
        await new AuditTrail(options, Hasher, TimeProvider.System).RecordAsync(Record(1), ct);
        await using var db = new RegReturnsDbContext(options);

        var refused = await Should.ThrowAsync<SqlException>(() =>
            db.Database.ExecuteSqlRawAsync("DELETE FROM audit.AuditEntries;", ct));

        refused.Number.ShouldBe(AppendOnlyViolation);
        (await LoadAsync(options, ct)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Ef_core_cannot_save_a_change_to_a_stored_entry()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = await CreateDatabaseAsync(ct);
        await new AuditTrail(options, Hasher, TimeProvider.System).RecordAsync(Record(1), ct);
        await using var db = new RegReturnsDbContext(options);
        var entry = await db.AuditEntries.SingleAsync(ct);

        db.Entry(entry).Property(e => e.Details).CurrentValue = "rewritten";

        var refused = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
        refused.InnerException.ShouldBeOfType<SqlException>().Number.ShouldBe(AppendOnlyViolation);
    }

    [Fact]
    public async Task Verification_detects_an_entry_edited_with_the_trigger_disabled()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = await CreateDatabaseAsync(ct);
        var trail = new AuditTrail(options, Hasher, TimeProvider.System);
        for (var i = 1; i <= 3; i++)
        {
            await trail.RecordAsync(Record(i), ct);
        }

        await WithTriggerDisabledAsync(options, "UPDATE audit.AuditEntries SET ActorDisplayName = N'Someone Else' WHERE Sequence = 2;", ct);

        var entries = await LoadAsync(options, ct);
        entries.Where(e => !Hasher.Verify(e)).Select(e => e.Sequence).ShouldBe([2]);
    }

    [Fact]
    public async Task Verification_detects_an_entry_deleted_with_the_trigger_disabled()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = await CreateDatabaseAsync(ct);
        var trail = new AuditTrail(options, Hasher, TimeProvider.System);
        for (var i = 1; i <= 3; i++)
        {
            await trail.RecordAsync(Record(i), ct);
        }

        await WithTriggerDisabledAsync(options, "DELETE FROM audit.AuditEntries WHERE Sequence = 2;", ct);

        // Every remaining entry still verifies on its own; the chain links expose the gap.
        var entries = await LoadAsync(options, ct);
        entries.Where(e => !Hasher.Verify(e)).ShouldBeEmpty();
        BrokenLinks(entries).ShouldBe([3]);
    }

    private static AuditRecord Record(int i) => new(
        AuditAction.AccessDenied,
        ActorType.User,
        $"subject-{i}",
        ActorDisplayName: $"Tester {i}",
        InstitutionCode: "ALPHA",
        Details: $"path=/submissions/{i}; reason=Bank.SubmitReturn",
        IpAddress: "10.1.2.3",
        CorrelationId: $"{i:D32}");

    /// <summary>Returns the sequence numbers whose position or link to the previous entry is wrong.</summary>
    private static List<long> BrokenLinks(List<AuditEntry> entries)
    {
        var broken = new List<long>();
        var expectedPrevious = AuditEntry.GenesisHash;
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Sequence != i + 1 || entries[i].PreviousHash != expectedPrevious)
            {
                broken.Add(entries[i].Sequence);
            }

            expectedPrevious = entries[i].Hash;
        }

        return broken;
    }

    private static async Task<List<AuditEntry>> LoadAsync(DbContextOptions<RegReturnsDbContext> options, CancellationToken ct)
    {
        await using var db = new RegReturnsDbContext(options);
        return await db.AuditEntries.AsNoTracking().OrderBy(e => e.Sequence).ToListAsync(ct);
    }

    private static async Task WithTriggerDisabledAsync(DbContextOptions<RegReturnsDbContext> options, string tamperSql, CancellationToken ct)
    {
        await using var db = new RegReturnsDbContext(options);
        await db.Database.ExecuteSqlRawAsync("DISABLE TRIGGER audit.TR_AuditEntries_AppendOnly ON audit.AuditEntries;", ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync(tamperSql, ct);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("ENABLE TRIGGER audit.TR_AuditEntries_AppendOnly ON audit.AuditEntries;", CancellationToken.None);
        }
    }

    private async Task<DbContextOptions<RegReturnsDbContext>> CreateDatabaseAsync(CancellationToken ct, bool withRetries = false)
    {
        var options = new DbContextOptionsBuilder<RegReturnsDbContext>()
            .UseSqlServer(sql.NewDatabaseConnectionString(), o =>
            {
                if (withRetries)
                {
                    o.EnableRetryOnFailure();
                }
            })
            .Options;
        await using var db = new RegReturnsDbContext(options);
        await new DatabaseInitializer(db, new FakeTimeProvider(SqlServerFixture.SeedDate), NullLogger<DatabaseInitializer>.Instance)
            .MigrateAsync(ct);
        return options;
    }
}
