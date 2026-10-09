using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

using RegReturns.Application.Identity;
using RegReturns.Domain.Identity;
using RegReturns.Domain.Institutions;
using RegReturns.IntegrationTests.Hosting;
using RegReturns.IntegrationTests.Infrastructure;
using RegReturns.Web.Navigation;

namespace RegReturns.IntegrationTests.Web;

/// <summary>
/// The administrator's directory (ADR 0031) against a database of this class's own: people and API clients are
/// listed, demo accounts and the administrator's own account cannot be changed, and a disabled person can no longer
/// act in the portal until re-enabled.
/// </summary>
[Collection(HostedAppsDefinition.Name)]
public sealed class UserAdministrationTests : IClassFixture<PortalDatabaseFixture>, IAsyncLifetime
{
    private static readonly string UsersPath = PortalAreas.Admin.Path + "/users";

    private readonly PortalDatabaseFixture _database;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SupervisionPortal _portal;

    public UserAdministrationTests(PortalDatabaseFixture database)
    {
        _database = database;
        _factory = PortalHost.Create(database.ConnectionString);
        _portal = new SupervisionPortal(_factory, database.ConnectionString);
    }

    public async ValueTask InitializeAsync()
    {
        // A person who is not a demo account, so the directory has an access form to take the antiforgery token from.
        await using var context = SqlServerFixture.CreateContext(_database.ConnectionString);
        if (!await context.Users.AnyAsync(u => u.UserName == "it.colleague", TestContext.Current.CancellationToken))
        {
            await context.Users.AddAsync(
                AppUser.Create("it.colleague", "A Colleague", "colleague@bov.example", null, [Role.Auditor]).Value,
                TestContext.Current.CancellationToken);
            var bank = await context.Institutions.SingleAsync(i => i.Code == "HLB", TestContext.Current.CancellationToken);
            var client = ApiClient.Create(bank, "regreturns-bank-hlb-directory", "Harbourline core banking");
            await context.ApiClients.AddAsync(client, TestContext.Current.CancellationToken);
            await context.Users.AddAsync(AppUser.ForApiClient(client), TestContext.Current.CancellationToken);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task The_directory_lists_people_and_api_clients_but_not_the_accounts_systems_act_through()
    {
        using var admin = await _portal.AdminAsync();

        var html = await PortalForms.GetTextAsync(admin, UsersPath);

        html.ShouldContain("data-user=\"maker.hlb\"");
        html.ShouldContain("data-user=\"it.colleague\"");
        html.ShouldContain("regreturns-bank-hlb-directory");
        html.ShouldContain("Harbourline core banking");
        html.ShouldNotContain(AppUser.ApiClientUserNamePrefix);
        html.ShouldNotContain(AppUser.MigrationUserName);
    }

    [Fact]
    public async Task Disabling_a_demo_account_is_refused()
    {
        using var admin = await _portal.AdminAsync();
        var auditor = await UserIdAsync("auditor");

        var response = await PortalForms.PostAsync(admin, UsersPath, $"{UsersPath}/{auditor}/disable");

        (await PortalForms.FollowAsync(admin, response)).ShouldContain(IdentityErrors.DemoAccountProtected.Message);
        (await StatusAsync("auditor")).ShouldBe(UserStatus.Active);
    }

    [Fact]
    public async Task A_disabled_person_can_no_longer_act_until_enabled_again()
    {
        using var admin = await _portal.AdminAsync();
        using var reviewer = await _portal.MultiRoleUserAsync("it.reviewer.access", [Role.SupervisorReviewer], bankCode: null);
        var id = await UserIdAsync("it.reviewer.access");
        (await PortalForms.GetTextAsync(reviewer, PortalAreas.Supervision.Path)).ShouldNotContain(ActorErrors.NotLinked.Message);

        var disabled = await PortalForms.PostAsync(admin, UsersPath, $"{UsersPath}/{id}/disable");

        (await PortalForms.FollowAsync(admin, disabled)).ShouldContain("Portal access disabled.");
        (await StatusAsync("it.reviewer.access")).ShouldBe(UserStatus.Disabled);
        (await PortalForms.GetTextAsync(reviewer, PortalAreas.Supervision.Path)).ShouldContain(ActorErrors.NotLinked.Message);

        var enabled = await PortalForms.PostAsync(admin, UsersPath, $"{UsersPath}/{id}/enable");

        (await PortalForms.FollowAsync(admin, enabled)).ShouldContain("Portal access re-enabled.");
        (await PortalForms.GetTextAsync(reviewer, PortalAreas.Supervision.Path)).ShouldNotContain(ActorErrors.NotLinked.Message);
    }

    [Fact]
    public async Task An_administrator_cannot_change_their_own_access()
    {
        using var admin = await _portal.MultiRoleUserAsync("it.admin.self", [Role.SystemAdmin], bankCode: null, withTotp: true);
        var self = await UserIdAsync("it.admin.self");

        var response = await PortalForms.PostAsync(admin, UsersPath, $"{UsersPath}/{self}/disable");

        (await PortalForms.FollowAsync(admin, response)).ShouldContain(UserAdministrationErrors.CannotChangeOwnAccess.Message);
        (await StatusAsync("it.admin.self")).ShouldBe(UserStatus.Active);
    }

    [Fact]
    public async Task An_unknown_user_is_not_found()
    {
        using var admin = await _portal.AdminAsync();

        var response = await PortalForms.PostAsync(admin, UsersPath, $"{UsersPath}/{Guid.CreateVersion7()}/disable");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_administrators_see_the_directory()
    {
        using var auditor = await _portal.AuditorAsync();

        var response = await auditor.GetAsync(new Uri(UsersPath, UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    public ValueTask DisposeAsync()
    {
        _factory.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<Guid> UserIdAsync(string userName)
    {
        await using var context = SqlServerFixture.CreateContext(_database.ConnectionString);
        return await context.Users.Where(u => u.UserName == userName).Select(u => u.Id).SingleAsync(TestContext.Current.CancellationToken);
    }

    private async Task<UserStatus> StatusAsync(string userName)
    {
        await using var context = SqlServerFixture.CreateContext(_database.ConnectionString);
        return await context.Users.Where(u => u.UserName == userName).Select(u => u.Status).SingleAsync(TestContext.Current.CancellationToken);
    }
}
