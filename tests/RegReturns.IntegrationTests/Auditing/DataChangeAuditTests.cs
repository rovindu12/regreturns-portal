using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using RegReturns.Application.Auditing;
using RegReturns.Domain.Auditing;
using RegReturns.Domain.Institutions;
using RegReturns.Infrastructure.Auditing;
using RegReturns.Infrastructure.Persistence;
using RegReturns.Infrastructure.Persistence.Seeding;
using RegReturns.IntegrationTests.Infrastructure;

namespace RegReturns.IntegrationTests.Auditing;

/// <summary>
/// Saves through a context with the data-change auditor (ADR 0024): every save appends an entry per changed aggregate
/// to the hash chain in the same transaction, a failed or rolled-back save leaves none, and concurrent saves share
/// one gap-free chain with the entries the audit trail records directly.
/// </summary>
public sealed class DataChangeAuditTests(SqlServerFixture sql)
{
    private static readonly AuditHasher Hasher = new(Options.Create(new AuditOptions { HmacKey = TestAuth.AuditKey }));

    private static readonly AuditOrigin Maker = new(
        new AuditActor(ActorType.User, "subject-maker", "Mara Maker", DemoBank.Harbourline), "10.1.2.3", new string('a', 32));

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero).AddTicks(1234567));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Seeding_a_new_database_writes_no_audit_entries()
    {
        var options = await CreateSeededDatabaseAsync();

        await using var db = new RegReturnsDbContext(options);
        (await db.Institutions.CountAsync(Ct)).ShouldBeGreaterThan(0);
        (await db.AuditEntries.CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Saving_a_change_appends_an_entry_with_the_actor_and_the_values_in_the_same_save()
    {
        var options = await CreateSeededDatabaseAsync();
        int written;
        string oldName;
        Guid bankId;
        await using (var db = Audited(options, Maker))
        {
            var bank = await db.Institutions.SingleAsync(i => i.Code == DemoBank.Harbourline, Ct);
            (bankId, oldName) = (bank.Id, bank.Name);
            bank.Rename("Harbourline Savings Bank");

            written = await db.SaveChangesAsync(Ct);

            db.ChangeTracker.Entries<AuditEntry>().ShouldBeEmpty();
            db.Entry(bank).State.ShouldBe(EntityState.Unchanged);
        }

        written.ShouldBe(2);
        var entry = (await LoadAsync(options)).ShouldHaveSingleItem();
        entry.Sequence.ShouldBe(1);
        entry.PreviousHash.ShouldBe(AuditEntry.GenesisHash);
        entry.Action.ShouldBe(AuditAction.Updated);
        entry.EntityType.ShouldBe(nameof(Institution));
        entry.EntityId.ShouldBe(bankId.ToString());
        new AuditOrigin(new AuditActor(entry.ActorType, entry.ActorSubjectId, entry.ActorDisplayName, entry.InstitutionCode), entry.IpAddress, entry.CorrelationId)
            .ShouldBe(Maker);
        entry.OccurredAt.ShouldBe(_clock.GetUtcNow());
        var item = AuditChanges.Parse(entry.Changes).ShouldNotBeNull().ShouldHaveSingleItem();
        item.ShouldBe(new AuditChangeItem(nameof(Institution), AuditChanges.Modified, item.Values));
        item.Values.ShouldHaveSingleItem().ShouldBe(new AuditValueChange(nameof(Institution.Name), oldName, "Harbourline Savings Bank"));
        Hasher.Verify(entry).ShouldBeTrue();
    }

    [Fact]
    public async Task Each_aggregate_changed_in_one_save_gets_its_own_linked_entry()
    {
        var options = await CreateSeededDatabaseAsync();
        await using (var db = Audited(options, Maker))
        {
            await db.Institutions.AddAsync(Institution.Create("NEWB", "New Bank", LicenceCategory.Commercial), Ct);
            (await db.Institutions.SingleAsync(i => i.Code == DemoBank.Harbourline, Ct)).Deactivate();

            await db.SaveChangesAsync(Ct);
        }

        var entries = await LoadAsync(options);
        entries.Select(e => e.Sequence).ShouldBe([1, 2]);
        entries.Select(e => e.Action).ShouldBe([AuditAction.Created, AuditAction.Updated], ignoreOrder: true);
        entries[1].PreviousHash.ShouldBe(entries[0].Hash);
        (await Verifier(options).VerifyAsync(Ct)).IsIntact.ShouldBeTrue();
    }

    [Fact]
    public async Task Save_that_fails_leaves_no_entry_and_the_next_save_starts_the_chain()
    {
        var options = await CreateSeededDatabaseAsync();
        await using (var db = Audited(options, Maker))
        {
            await db.Institutions.AddAsync(Institution.Create(DemoBank.Harbourline, "Duplicate Harbourline", LicenceCategory.Commercial), Ct);

            await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));

            db.ChangeTracker.Entries<AuditEntry>().ShouldBeEmpty();
        }

        (await LoadAsync(options)).ShouldBeEmpty();
        await RenameAsync(options, DemoBank.Harbourline, "Harbourline Savings Bank");
        (await LoadAsync(options)).ShouldHaveSingleItem().PreviousHash.ShouldBe(AuditEntry.GenesisHash);
    }

    [Fact]
    public async Task Save_in_a_transaction_that_is_rolled_back_leaves_no_entry()
    {
        var options = await CreateSeededDatabaseAsync();
        await using (var db = Audited(options, Maker))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(Ct);
            (await db.Institutions.SingleAsync(i => i.Code == DemoBank.Harbourline, Ct)).Rename("Harbourline Savings Bank");
            await db.SaveChangesAsync(Ct);
            (await db.AuditEntries.CountAsync(Ct)).ShouldBe(1);

            await transaction.RollbackAsync(Ct);
        }

        (await LoadAsync(options)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Unchanged_save_writes_no_entry()
    {
        var options = await CreateSeededDatabaseAsync();
        await using (var db = Audited(options, Maker))
        {
            var bank = await db.Institutions.SingleAsync(i => i.Code == DemoBank.Harbourline, Ct);
            bank.Rename(bank.Name);

            (await db.SaveChangesAsync(Ct)).ShouldBe(0);
        }

        (await LoadAsync(options)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Concurrent_saves_and_recorded_events_share_one_gap_free_chain()
    {
        var options = await CreateSeededDatabaseAsync(withRetries: true);
        const int Writers = 10;
        var trail = new AuditTrail(options, Hasher, TimeProvider.System);
        using var start = new SemaphoreSlim(0);

        var saves = Enumerable.Range(1, Writers).Select(i => Task.Run(
            async () =>
            {
                await start.WaitAsync(Ct);
                await using var db = Audited(options, Maker, TimeProvider.System);
                await db.Institutions.AddAsync(Institution.Create($"CB{i:D2}", $"Concurrent Bank {i}", LicenceCategory.Commercial), Ct);
                await db.SaveChangesAsync(Ct);
            },
            Ct));
        var records = Enumerable.Range(1, Writers).Select(i => Task.Run(
            async () =>
            {
                await start.WaitAsync(Ct);
                await trail.RecordAsync(Maker.ToRecord(AuditAction.AccessDenied, $"path=/supervision; attempt={i}"), Ct);
            },
            Ct));
        var all = saves.Concat(records).ToList();
        start.Release(all.Count);
        await Task.WhenAll(all);

        var entries = await LoadAsync(options);
        entries.Select(e => e.Sequence).ShouldBe(Enumerable.Range(1, 2 * Writers).Select(i => (long)i));
        entries.Count(e => e.Action == AuditAction.Created).ShouldBe(Writers);
        var verification = await Verifier(options).VerifyAsync(Ct);
        verification.IsIntact.ShouldBeTrue();
        verification.EntriesChecked.ShouldBe(2 * Writers);
    }

    private RegReturnsDbContext Audited(DbContextOptions<RegReturnsDbContext> options, AuditOrigin origin, TimeProvider? clock = null) =>
        new(options, new DataChangeAuditor(new FixedAuditContext(origin), Hasher, clock ?? _clock));

    private static AuditChainVerifier Verifier(DbContextOptions<RegReturnsDbContext> options) =>
        new(options, Hasher, NullLogger<AuditChainVerifier>.Instance);

    private async Task RenameAsync(DbContextOptions<RegReturnsDbContext> options, string code, string name)
    {
        await using var db = Audited(options, Maker);
        (await db.Institutions.SingleAsync(i => i.Code == code, Ct)).Rename(name);
        await db.SaveChangesAsync(Ct);
    }

    private static async Task<List<AuditEntry>> LoadAsync(DbContextOptions<RegReturnsDbContext> options)
    {
        await using var db = new RegReturnsDbContext(options);
        return await db.AuditEntries.AsNoTracking().OrderBy(e => e.Sequence).ToListAsync(Ct);
    }

    private async Task<DbContextOptions<RegReturnsDbContext>> CreateSeededDatabaseAsync(bool withRetries = false)
    {
        var options = new DbContextOptionsBuilder<RegReturnsDbContext>()
            .UseSqlServer(sql.NewDatabaseConnectionString(), o =>
            {
                o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                if (withRetries)
                {
                    o.EnableRetryOnFailure();
                }
            })
            .Options;
        await using var db = new RegReturnsDbContext(options);
        var initializer = new DatabaseInitializer(db, new FakeTimeProvider(SqlServerFixture.SeedDate), NullLogger<DatabaseInitializer>.Instance);
        await initializer.MigrateAsync(Ct);
        await initializer.SeedAsync(Ct);
        return options;
    }

    private sealed class FixedAuditContext(AuditOrigin origin) : IAuditContext
    {
        public AuditOrigin Current => origin;
    }
}
