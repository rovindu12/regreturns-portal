using System.Data;
using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using RegReturns.Application.Auditing;
using RegReturns.Application.Demo;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Auditing;
using RegReturns.Domain.Common;
using RegReturns.Domain.Identity;
using RegReturns.Domain.Institutions;
using RegReturns.Domain.Periods;
using RegReturns.IntegrationTests.Hosting;
using RegReturns.IntegrationTests.Infrastructure;
using RegReturns.Web.Navigation;

namespace RegReturns.IntegrationTests.Web;

/// <summary>
/// The demo reset (ADR 0031) against a freshly seeded database per test, since every reset replaces the workload: the
/// administrator's button, the cooldown, the scheduled reset acting as the system, the lock and the interlock that
/// keeps a database with real people from ever being reset.
/// </summary>
[Collection(HostedAppsDefinition.Name)]
public sealed class DemoResetTests(SqlServerFixture sql) : IAsyncLifetime
{
    private static readonly string ResetPath = PortalAreas.Admin.Path + "/demo/reset";

    // Three months after the seed: a reset files returns up to December 2026.
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2027, 1, 15, 9, 0, 0, TimeSpan.Zero));
    private readonly PortalDatabaseFixture _database = new(sql);
    private WebApplicationFactory<Program> _factory = null!;
    private SupervisionPortal _portal = null!;

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        _factory = Host(demoEnabled: true);
        _portal = new SupervisionPortal(_factory, _database.ConnectionString);
    }

    [Fact]
    public async Task An_administrator_reset_replaces_the_workload_and_keeps_the_directory_and_the_audit_trail()
    {
        await RegisterApiClientAsync();
        using var admin = await _portal.AdminAsync();
        var before = await SnapshotAsync();

        var response = await PortalForms.PostAsync(admin, PortalAreas.Admin.Path, ResetPath);

        (await PortalForms.FollowAsync(admin, response)).ShouldContain("The demo was reset: ");
        var after = await SnapshotAsync();
        after.Institutions.ShouldBe(before.Institutions, ignoreOrder: true);
        after.Users.ShouldBe(before.Users, ignoreOrder: true);
        after.ApiClients.ShouldBe(before.ApiClients, ignoreOrder: true);
        after.Submissions.Intersect(before.Submissions).ShouldBeEmpty();
        after.LatestMonth.ShouldBe("2026-12");
        before.LatestMonth.ShouldBe("2026-09");

        after.AuditSequences.Take(before.AuditSequences.Count).ShouldBe(before.AuditSequences);
        var entry = await LatestEntryAsync();
        entry.Action.ShouldBe(AuditAction.DemoReset);
        entry.ActorType.ShouldBe(ActorType.User);
        entry.ActorSubjectId.ShouldBe("it-admin.demo");
        entry.Details.ShouldNotBeNull().ShouldStartWith("Manual demo reset; removed ");
        (await VerifyChainAsync()).IsIntact.ShouldBeTrue();
    }

    [Fact]
    public async Task A_second_reset_within_the_cooldown_is_refused_until_it_has_passed()
    {
        using var admin = await _portal.AdminAsync();
        await PortalForms.PostAsync(admin, PortalAreas.Admin.Path, ResetPath);

        var refused = await PortalForms.PostAsync(admin, PortalAreas.Admin.Path, ResetPath);
        var page = await PortalForms.FollowAsync(admin, refused);

        page.ShouldContain("It can be reset again from 09:10 UTC.");
        page.ShouldContain("disabled=\"disabled\">Reset demo</button>");
        (await DemoResetCountAsync()).ShouldBe(1);

        _time.Advance(TimeSpan.FromMinutes(10));
        var accepted = await PortalForms.PostAsync(admin, PortalAreas.Admin.Path, ResetPath);
        (await PortalForms.FollowAsync(admin, accepted)).ShouldContain("The demo was reset");
        (await DemoResetCountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task The_scheduled_reset_acts_as_the_system_and_ignores_the_cooldown()
    {
        using var admin = await _portal.AdminAsync();
        await PortalForms.PostAsync(admin, PortalAreas.Admin.Path, ResetPath);

        var result = await ScheduledResetAsync();

        result.IsSuccess.ShouldBeTrue();
        var entry = await LatestEntryAsync();
        entry.Sequence.ShouldBe(result.Value.AuditSequence);
        entry.ActorType.ShouldBe(ActorType.System);
        entry.Details.ShouldNotBeNull().ShouldStartWith("Scheduled demo reset; ");
        (await VerifyChainAsync()).IsIntact.ShouldBeTrue();
    }

    [Fact]
    public async Task A_reset_is_refused_while_another_one_holds_the_lock()
    {
        var before = await SnapshotAsync();
        await using var connection = new SqlConnection(_database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using (var command = new SqlCommand(
            "EXEC sp_getapplock @Resource = N'RegReturns.DemoReset', @LockMode = N'Exclusive', @LockOwner = N'Transaction';",
            connection,
            transaction))
        {
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var result = await ScheduledResetAsync();

        result.Error.ShouldBe(DemoErrors.InProgress);
        (await SnapshotAsync()).Submissions.ShouldBe(before.Submissions, ignoreOrder: true);
    }

    [Fact]
    public async Task A_database_holding_people_who_are_not_demo_accounts_is_never_reset()
    {
        await using (var context = SqlServerFixture.CreateContext(_database.ConnectionString))
        {
            var person = AppUser.Create("real.person", "A Real Person", "real.person@bov.example", null, [Role.Auditor]).Value;
            await context.Users.AddAsync(person, TestContext.Current.CancellationToken);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var before = await SnapshotAsync();
        using var admin = await _portal.AdminAsync();

        var response = await PortalForms.PostAsync(admin, PortalAreas.Admin.Path, ResetPath);

        (await PortalForms.FollowAsync(admin, response)).ShouldContain(DemoErrors.NotADemoDatabase.Message);
        (await ScheduledResetAsync()).Error.ShouldBe(DemoErrors.NotADemoDatabase);
        (await SnapshotAsync()).Submissions.ShouldBe(before.Submissions, ignoreOrder: true);
        (await DemoResetCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Outside_demo_mode_nothing_can_be_reset()
    {
        await _factory.DisposeAsync();
        _factory = Host(demoEnabled: false);

        (await ScheduledResetAsync()).Error.ShouldBe(DemoErrors.Disabled);
        (await DemoResetCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Only_an_administrator_can_press_the_button()
    {
        using var reviewer = await _portal.ReviewerAsync();
        using var content = new FormUrlEncodedContent([]);

        var response = await reviewer.PostAsync(new Uri(ResetPath, UriKind.Relative), content, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await DemoResetCountAsync()).ShouldBe(0);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _database.DisposeAsync();
    }

    private WebApplicationFactory<Program> Host(bool demoEnabled) => PortalHost.Create(
        _database.ConnectionString,
        services => services.AddSingleton<TimeProvider>(_time),
        new Dictionary<string, string?> { ["Demo:Enabled"] = demoEnabled ? "true" : "false" });

    private async Task<Result<DemoResetReport>> ScheduledResetAsync()
    {
        // Outside a request the portal's audit context is the system, as for the nightly job.
        await using var scope = _factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ResetDemo, Result<DemoResetReport>>>();
        return await handler.HandleAsync(new ResetDemo(DemoResetTrigger.Scheduled), TestContext.Current.CancellationToken);
    }

    private async Task<ChainVerification> VerifyChainAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAuditChainVerifier>().VerifyAsync(TestContext.Current.CancellationToken);
    }

    private async Task RegisterApiClientAsync()
    {
        await using var context = SqlServerFixture.CreateContext(_database.ConnectionString);
        var bank = await context.Institutions.SingleAsync(i => i.Code == "HLB", TestContext.Current.CancellationToken);
        var client = ApiClient.Create(bank, "regreturns-bank-hlb-reset", "Harbourline core banking");
        await context.ApiClients.AddAsync(client, TestContext.Current.CancellationToken);
        await context.Users.AddAsync(AppUser.ForApiClient(client), TestContext.Current.CancellationToken);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<AuditEntry> LatestEntryAsync()
    {
        await using var context = SqlServerFixture.CreateContext(_database.ConnectionString);
        return await context.AuditEntries.AsNoTracking().OrderByDescending(e => e.Sequence).FirstAsync(TestContext.Current.CancellationToken);
    }

    private async Task<int> DemoResetCountAsync()
    {
        await using var context = SqlServerFixture.CreateContext(_database.ConnectionString);
        return await context.AuditEntries.CountAsync(e => e.Action == AuditAction.DemoReset, TestContext.Current.CancellationToken);
    }

    private async Task<Snapshot> SnapshotAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var context = SqlServerFixture.CreateContext(_database.ConnectionString);
        var months = await context.Obligations.AsNoTracking()
            .Where(o => o.Period.Frequency == ReturnFrequency.Monthly)
            .Select(o => o.Period)
            .ToListAsync(ct);
        return new Snapshot(
            await context.Institutions.Select(i => i.Id).ToListAsync(ct),
            await context.Users.Select(u => u.Id).ToListAsync(ct),
            await context.ApiClients.Select(c => c.Id).ToListAsync(ct),
            await context.Submissions.Select(s => s.Id).ToListAsync(ct),
            await context.AuditEntries.OrderBy(e => e.Sequence).Select(e => e.Sequence).ToListAsync(ct),
            months.Max()!.Label);
    }

    private sealed record Snapshot(
        List<Guid> Institutions,
        List<Guid> Users,
        List<Guid> ApiClients,
        List<Guid> Submissions,
        List<long> AuditSequences,
        string LatestMonth);
}
