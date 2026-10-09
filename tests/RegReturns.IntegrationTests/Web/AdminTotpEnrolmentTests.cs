using System.Collections.Concurrent;
using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

using RegReturns.Application.Identity;
using RegReturns.Domain.Auditing;
using RegReturns.Domain.Identity;
using RegReturns.Infrastructure.Persistence.Seeding;
using RegReturns.IntegrationTests.Hosting;
using RegReturns.IntegrationTests.Infrastructure;

namespace RegReturns.IntegrationTests.Web;

/// <summary>
/// Resetting a person's authenticator from the directory page (ADR 0032), with WSO2 replaced by an in-memory directory:
/// it works by user name for someone who has never signed in, opens a window of the configured length, is audited, and
/// refuses demo, system and own accounts, accounts without a portal role and a WSO2 that cannot be reached.
/// </summary>
[Collection(HostedAppsDefinition.Name)]
public sealed class AdminTotpEnrolmentTests : IClassFixture<PortalDatabaseFixture>, IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 8, 30, 0, TimeSpan.Zero);

    private readonly FakeDirectory _directory = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SupervisionPortal _supervision;
    private readonly string _connectionString;

    public AdminTotpEnrolmentTests(PortalDatabaseFixture database)
    {
        _connectionString = database.ConnectionString;
        _factory = PortalHost.Create(
            database.ConnectionString,
            services =>
            {
                services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
                services.Replace(ServiceDescriptor.Singleton<IIdentityDirectory>(_directory));
            },
            new Dictionary<string, string?> { ["Iam:TotpEnrolment:WindowHours"] = "48" });
        _supervision = new SupervisionPortal(_factory, database.ConnectionString);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_directory_page_offers_the_reset_with_the_window_length()
    {
        using var admin = await _supervision.AdminAsync();

        var page = await PortalForms.GetTextAsync(admin, "/admin/users");

        page.ShouldContain("Reset an authenticator");
        page.ShouldContain("within 48 hours");
        page.ShouldContain("action=\"/admin/users/totp-enrolment\"");
    }

    [Fact]
    public async Task A_person_who_never_signed_in_gets_a_window_in_wso2_and_an_audit_entry()
    {
        _directory.Add("new.approver", "wso2-new-approver", RoleNames.SupervisorApprover);
        using var admin = await _supervision.AdminAsync();

        var response = await PostAsync(admin, " New.Approver ");

        var page = await PortalForms.FollowAsync(admin, response);
        page.ShouldContain("new.approver can set up a new authenticator at their next sign-in until 11 October 2026, 08:30 UTC");
        _directory.Windows["wso2-new-approver"].ShouldBe(Now.AddHours(48));
        var entry = await LastEnrolmentEntryAsync();
        entry.ShouldNotBeNull();
        entry.EntityId.ShouldBe("wso2-new-approver");
        entry.ActorSubjectId.ShouldBe($"it-{DemoUsers.Admin}");
        entry.Details.ShouldNotBeNull().ShouldContain("\"userName\":\"new.approver\"");
        entry.Details.ShouldContain("\"until\":\"2026-10-11T08:30:00.0000000Z\"");
    }

    [Fact]
    public async Task A_linked_person_who_lost_their_phone_can_be_reset()
    {
        await AddPersonAsync("lost.phone", Role.SupervisorApprover, wso2UserId: "wso2-lost-phone");
        _directory.Add("lost.phone", "wso2-lost-phone", RoleNames.SupervisorApprover);
        using var admin = await _supervision.AdminAsync();

        var page = await PortalForms.FollowAsync(admin, await PostAsync(admin, "lost.phone"));

        page.ShouldContain("lost.phone can set up a new authenticator");
        _directory.Windows.ShouldContainKey("wso2-lost-phone");
    }

    [Theory]
    [InlineData(DemoUsers.ApproverMfa, "Demo accounts cannot be changed.")]
    [InlineData(AppUser.MigrationUserName, "API client and system accounts cannot be changed here.")]
    [InlineData("api-client.hlb-core", "API client and system accounts cannot be changed here.")]
    [InlineData(DemoUsers.Admin, "You cannot reset your own authenticator.")]
    [InlineData("not a user name", "Enter a user name")]
    [InlineData("", "Enter a user name")]
    public async Task Protected_and_invalid_accounts_are_refused_without_asking_wso2(string userName, string message)
    {
        using var admin = await _supervision.AdminAsync();

        var page = await PortalForms.FollowAsync(admin, await PostAsync(admin, userName));

        page.ShouldContain(message);
        _directory.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_name_wso2_does_not_know_is_refused()
    {
        using var admin = await _supervision.AdminAsync();

        var page = await PortalForms.FollowAsync(admin, await PostAsync(admin, "nobody.here"));

        page.ShouldContain("WSO2 has no account with this user name.");
    }

    [Fact]
    public async Task An_account_without_a_portal_role_is_not_the_portals_to_change()
    {
        _directory.Add("wso2.operator", "wso2-operator", "everyone-else");
        using var admin = await _supervision.AdminAsync();

        var page = await PortalForms.FollowAsync(admin, await PostAsync(admin, "wso2.operator"));

        page.ShouldContain("This account holds no portal role.");
        _directory.Windows.ShouldNotContainKey("wso2-operator");
    }

    [Fact]
    public async Task A_disabled_person_must_be_re_enabled_first()
    {
        await AddPersonAsync("left.bank", Role.SupervisorReviewer, wso2UserId: "wso2-left-bank", disabled: true);
        _directory.Add("left.bank", "wso2-left-bank", RoleNames.SupervisorReviewer);
        using var admin = await _supervision.AdminAsync();

        var page = await PortalForms.FollowAsync(admin, await PostAsync(admin, "left.bank"));

        page.ShouldContain("Re-enable it before resetting their authenticator.");
        _directory.Windows.ShouldNotContainKey("wso2-left-bank");
    }

    [Fact]
    public async Task A_different_wso2_account_under_a_linked_name_is_refused()
    {
        await AddPersonAsync("renamed.user", Role.SupervisorReviewer, wso2UserId: "wso2-original");
        _directory.Add("renamed.user", "wso2-impostor", RoleNames.SupervisorReviewer);
        using var admin = await _supervision.AdminAsync();

        var page = await PortalForms.FollowAsync(admin, await PostAsync(admin, "renamed.user"));

        page.ShouldContain("linked to a different WSO2 account");
        _directory.Windows.ShouldNotContainKey("wso2-impostor");
    }

    [Fact]
    public async Task When_wso2_cannot_be_reached_nothing_changes_and_nothing_is_audited()
    {
        _directory.Add("unlucky.user", "wso2-unlucky", RoleNames.SystemAdmin);
        _directory.FailFor("wso2-unlucky");
        using var admin = await _supervision.AdminAsync();
        var before = await EnrolmentEntriesAsync();

        var page = await PortalForms.FollowAsync(admin, await PostAsync(admin, "unlucky.user"));

        page.ShouldContain("WSO2 could not be reached or refused the change. Nothing was changed");
        (await EnrolmentEntriesAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task Only_administrators_with_two_step_sign_in_may_reset()
    {
        using var reviewer = await _supervision.ReviewerAsync();
        using var form = new FormUrlEncodedContent([new KeyValuePair<string, string>("userName", "new.approver")]);

        // The policy refuses before the form is even read.
        var response = await reviewer.PostAsync(new Uri("/admin/users/totp-enrolment", UriKind.Relative), form, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        _directory.Calls.ShouldBe(0);
    }

    public void Dispose() => _factory.Dispose();

    private static Task<HttpResponseMessage> PostAsync(HttpClient admin, string userName) =>
        PortalForms.PostAsync(admin, "/admin/users", "/admin/users/totp-enrolment", ("userName", userName));

    private async Task AddPersonAsync(string userName, Role role, string wso2UserId, bool disabled = false)
    {
        await using var context = SqlServerFixture.CreateContext(_connectionString);
        if (await context.Users.AnyAsync(u => u.UserName == userName, Ct))
        {
            return;
        }

        var user = AppUser.Create(userName, $"Test {userName}", $"{userName}@test.example", null, [role]).Value;
        user.LinkIdentity(wso2UserId);
        if (disabled)
        {
            user.Disable().IsSuccess.ShouldBeTrue();
        }

        await context.Users.AddAsync(user, Ct);
        await context.SaveChangesAsync(Ct);
    }

    private async Task<AuditEntry?> LastEnrolmentEntryAsync()
    {
        await using var context = SqlServerFixture.CreateContext(_connectionString);
        return await context.AuditEntries.AsNoTracking()
            .Where(e => e.Action == AuditAction.TotpEnrolmentOpened)
            .OrderByDescending(e => e.Sequence)
            .FirstOrDefaultAsync(Ct);
    }

    private async Task<int> EnrolmentEntriesAsync()
    {
        await using var context = SqlServerFixture.CreateContext(_connectionString);
        return await context.AuditEntries.CountAsync(e => e.Action == AuditAction.TotpEnrolmentOpened, Ct);
    }

    /// <summary>WSO2 as a dictionary: accounts by user name, and the windows opened.</summary>
    private sealed class FakeDirectory : IIdentityDirectory
    {
        private readonly ConcurrentDictionary<string, IdentityAccount> _accounts = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, bool> _failing = new(StringComparer.Ordinal);
        private int _calls;

        public ConcurrentDictionary<string, DateTimeOffset> Windows { get; } = new(StringComparer.Ordinal);

        public int Calls => _calls;

        public void Add(string userName, string id, params string[] roles) => _accounts[userName] = new IdentityAccount(id, userName, roles);

        public void FailFor(string id) => _failing[id] = true;

        public Task<IdentityAccount?> FindByUserNameAsync(string userName, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(_accounts.GetValueOrDefault(userName));
        }

        public Task OpenTotpEnrolmentAsync(string accountId, DateTimeOffset until, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            if (_failing.ContainsKey(accountId))
            {
                return Task.FromException(new IdentityDirectoryException("WSO2 answered PATCH /scim2/Users with 503."));
            }

            Windows[accountId] = until;
            return Task.CompletedTask;
        }
    }
}
