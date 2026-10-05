using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using RegReturns.Application.Idempotency;
using RegReturns.Infrastructure.Idempotency;
using RegReturns.Infrastructure.Persistence;
using RegReturns.IntegrationTests.Infrastructure;

namespace RegReturns.IntegrationTests.Api;

/// <summary>A migrated, empty database of its own for the idempotency store tests.</summary>
/// <param name="sql">The shared SQL Server container.</param>
public sealed class IdempotencyDatabaseFixture(SqlServerFixture sql) : IAsyncLifetime
{
    /// <summary>Gets the connection string of this fixture's database.</summary>
    public string ConnectionString { get; } = sql.NewDatabaseConnectionString();

    public async ValueTask InitializeAsync()
    {
        await using var context = SqlServerFixture.CreateContext(ConnectionString);
        var initializer = new DatabaseInitializer(context, new FakeTimeProvider(SqlServerFixture.SeedDate), NullLogger<DatabaseInitializer>.Instance);
        await initializer.MigrateAsync(CancellationToken.None);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// The SQL Server idempotency store (ADR 0027). Every test uses its own client id, so keys never collide across tests.
/// </summary>
public sealed class IdempotencyStoreTests(IdempotencyDatabaseFixture database) : IClassFixture<IdempotencyDatabaseFixture>
{
    private static readonly IdempotencyOptions Settings = new();

    private static readonly string Body = new('a', 64);

    private static readonly string OtherBody = new('b', 64);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));

    private readonly string _clientId = $"client-{Guid.NewGuid():N}";

    private static StoredResponse Created => new(201, "application/json; charset=utf-8", "/v1/submissions/1", "{\"created\":true}"u8.ToArray());

    [Fact]
    public async Task A_new_key_starts_the_request()
    {
        var start = await Store().BeginAsync(Request("k1"), TestContext.Current.CancellationToken);

        start.Outcome.ShouldBe(IdempotencyOutcome.Started);
        start.RecordId.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_completed_request_is_replayed_with_its_stored_response()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Store();
        var first = await store.BeginAsync(Request("k1"), ct);
        await store.CompleteAsync(first.RecordId!.Value, Created, ct);

        var again = await store.BeginAsync(Request("k1"), ct);

        again.Outcome.ShouldBe(IdempotencyOutcome.Replay);
        again.Response!.StatusCode.ShouldBe(201);
        again.Response.ContentType.ShouldBe(Created.ContentType);
        again.Response.Location.ShouldBe(Created.Location);
        again.Response.Body.ShouldBe(Created.Body);
    }

    [Fact]
    public async Task The_same_key_with_another_request_is_refused_as_reused()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Store();
        var first = await store.BeginAsync(Request("k1"), ct);
        await store.CompleteAsync(first.RecordId!.Value, Created, ct);

        var other = await store.BeginAsync(Request("k1", OtherBody), ct);

        other.Outcome.ShouldBe(IdempotencyOutcome.KeyReused);
    }

    [Fact]
    public async Task A_request_still_running_is_in_progress()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Store();
        await store.BeginAsync(Request("k1"), ct);

        var again = await store.BeginAsync(Request("k1"), ct);

        again.Outcome.ShouldBe(IdempotencyOutcome.InProgress);
    }

    [Fact]
    public async Task A_claim_that_outlived_its_lock_is_taken_over()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Store();
        var first = await store.BeginAsync(Request("k1"), ct);
        _clock.Advance(TimeSpan.FromSeconds(Settings.InFlightSeconds + 1));

        var again = await store.BeginAsync(Request("k1"), ct);

        again.Outcome.ShouldBe(IdempotencyOutcome.Started);
        again.RecordId.ShouldBe(first.RecordId);
    }

    [Fact]
    public async Task A_released_key_can_start_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Store();
        var first = await store.BeginAsync(Request("k1"), ct);
        await store.ReleaseAsync(first.RecordId!.Value, ct);

        var again = await store.BeginAsync(Request("k1", OtherBody), ct);

        again.Outcome.ShouldBe(IdempotencyOutcome.Started);
    }

    [Fact]
    public async Task An_expired_key_starts_over_for_any_request()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Store();
        var first = await store.BeginAsync(Request("k1"), ct);
        await store.CompleteAsync(first.RecordId!.Value, Created, ct);
        _clock.Advance(TimeSpan.FromHours(Settings.RetentionHours));

        var again = await store.BeginAsync(Request("k1", OtherBody), ct);

        again.Outcome.ShouldBe(IdempotencyOutcome.Started);
        (await store.BeginAsync(Request("k1", OtherBody), ct)).Outcome.ShouldBe(IdempotencyOutcome.InProgress);
    }

    [Fact]
    public async Task Keys_belong_to_one_client()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Store();
        await store.BeginAsync(Request("k1"), ct);

        var otherClient = await store.BeginAsync(new IdempotentRequest($"{_clientId}-other", "k1", Body), ct);

        otherClient.Outcome.ShouldBe(IdempotencyOutcome.Started);
    }

    [Fact]
    public async Task Purging_deletes_expired_records_only()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = Store();
        await store.BeginAsync(Request("old"), ct);
        _clock.Advance(TimeSpan.FromHours(Settings.RetentionHours));
        await store.BeginAsync(Request("new"), ct);

        var purged = await store.PurgeExpiredAsync(ct);

        purged.ShouldBeGreaterThanOrEqualTo(1);
        await using var db = SqlServerFixture.CreateContext(database.ConnectionString);
        var keys = await db.Set<IdempotencyRecord>().Where(r => r.ClientId == _clientId).Select(r => r.Key).ToListAsync(ct);
        keys.ShouldBe(["new"]);
    }

    private IdempotentRequest Request(string key, string? fingerprint = null) => new(_clientId, key, fingerprint ?? Body);

    private IdempotencyStore Store() => new(
        new DbContextOptionsBuilder<RegReturnsDbContext>().UseSqlServer(database.ConnectionString).Options,
        Options.Create(Settings),
        _clock,
        NullLogger<IdempotencyStore>.Instance);
}
