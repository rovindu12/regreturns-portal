using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using RegReturns.Application.Authorization;
using RegReturns.Application.Identity;
using RegReturns.Domain.Auditing;
using RegReturns.IntegrationTests.Hosting;
using RegReturns.IntegrationTests.Infrastructure;
using RegReturns.Web.Navigation;

namespace RegReturns.IntegrationTests.Web;

[Collection(HostedAppsDefinition.Name)]
public sealed class PortalAuthorizationTests : IClassFixture<PortalDatabaseFixture>, IDisposable
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    private readonly PortalDatabaseFixture _database;
    private readonly WebApplicationFactory<Program> _factory;

    public PortalAuthorizationTests(PortalDatabaseFixture database)
    {
        _database = database;

        // A fixed clock keeps both requests of the de-duplication test in the same minute.
        _factory = PortalHost.Create(
            database.ConnectionString,
            services => services.AddSingleton<TimeProvider>(new FakeTimeProvider(SqlServerFixture.SeedDate)));
    }

    [Fact]
    public async Task Anonymous_request_to_a_protected_page_is_challenged()
    {
        using var client = Anonymous();

        var response = await client.GetAsync(PortalAreas.Bank.Path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Home_page_is_open_to_anonymous_visitors()
    {
        using var client = Anonymous();

        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("Sign in");
    }

    [Fact]
    public async Task Static_assets_are_open_to_anonymous_visitors()
    {
        using var client = Anonymous();

        var response = await client.GetAsync("/css/site.css", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Maker_opens_the_bank_area()
    {
        using var client = Maker("maker-opens-bank");

        var response = await client.GetAsync(PortalAreas.Bank.Path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        html.ShouldContain("Maker (Harbourline Bank PLC)");
        html.ShouldContain("<h1 class=\"h2\">Bank returns</h1>");
    }

    [Fact]
    public async Task Navigation_lists_only_the_areas_the_user_may_open()
    {
        using var client = Maker("maker-navigation");

        var html = await client.GetStringAsync("/", TestContext.Current.CancellationToken);

        html.ShouldContain($"href=\"{PortalAreas.Bank.Path}\"");
        html.ShouldContain($"href=\"{PortalAreas.Reports.Path}\"");
        html.ShouldNotContain($"href=\"{PortalAreas.Supervision.Path}\"");
        html.ShouldNotContain($"href=\"{PortalAreas.Admin.Path}\"");
    }

    [Fact]
    public async Task Maker_is_forbidden_from_supervision_and_one_audit_entry_is_written_per_minute()
    {
        const string subject = "maker-denied-supervision";
        using var client = Maker(subject);

        var first = await client.GetAsync(PortalAreas.Supervision.Path, TestContext.Current.CancellationToken);
        var second = await client.GetAsync(PortalAreas.Supervision.Path, TestContext.Current.CancellationToken);

        first.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        second.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var entries = await AuditEntriesAsync(subject, AuditAction.AccessDenied);
        entries.Count.ShouldBe(1);
        entries[0].Details.ShouldBe($"path={PortalAreas.Supervision.Path}; reason={Policies.SupervisionAccess}");
    }

    [Fact]
    public async Task Administrator_without_two_step_verification_is_forbidden_from_administration()
    {
        using var client = SignedIn(
            (ClaimNames.Subject, "admin-without-totp"), (ClaimNames.Name, "Portal Admin"), (ClaimNames.Roles, RoleNames.SystemAdmin),
            (ClaimNames.AuthenticationMethods, "BasicAuthenticator"));

        var response = await client.GetAsync(PortalAreas.Admin.Path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Administrator_with_two_step_verification_opens_administration()
    {
        using var client = SignedIn(
            (ClaimNames.Subject, "admin-with-totp"), (ClaimNames.Name, "Portal Admin"), (ClaimNames.Roles, RoleNames.SystemAdmin),
            (ClaimNames.AuthenticationMethods, "BasicAuthenticator"), (ClaimNames.AuthenticationMethods, "totp"));

        var response = await client.GetAsync(PortalAreas.Admin.Path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sign_out_without_an_antiforgery_token_is_rejected()
    {
        using var client = Maker("maker-sign-out");

        var response = await client.PostAsync("/Account/SignOut", new FormUrlEncodedContent([]), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Signed_in_user_is_never_sent_off_site_after_sign_in()
    {
        using var client = Maker("maker-return-url");

        var response = await client.GetAsync("/Account/SignIn?returnUrl=https%3A%2F%2Fevil.example%2F", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe("/");
    }

    [Fact]
    public async Task Sign_in_failure_page_explains_the_reason_and_shows_the_error_reference()
    {
        using var client = Anonymous();

        var html = await client.GetStringAsync(
            $"/Account/SignInFailed?reason={LinkSignedInUserHandler.UnknownInstitution.Code}&reference={TraceId}",
            TestContext.Current.CancellationToken);

        html.ShouldContain("institution the portal does not know");
        html.ShouldContain(TraceId);
    }

    public void Dispose() => _factory.Dispose();

    private HttpClient Anonymous() => _factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = PortalHost.BaseAddress });

    private HttpClient Maker(string subject) => SignedIn(
        (ClaimNames.Subject, subject),
        (ClaimNames.UserName, "maker"),
        (ClaimNames.Name, "Maker (Harbourline Bank PLC)"),
        (ClaimNames.Roles, RoleNames.BankMaker),
        (ClaimNames.InstitutionId, "HBL"));

    // Signed-in pages render the antiforgery-protected sign-out form, whose cookie is Secure-only: use HTTPS.
    private HttpClient SignedIn(params (string Type, string Value)[] claims)
    {
        var client = _factory.CreateClientAs(claims);
        client.BaseAddress = PortalHost.BaseAddress;
        return client;
    }

    private async Task<List<AuditEntry>> AuditEntriesAsync(string subject, AuditAction action)
    {
        await using var context = SqlServerFixture.CreateContext(_database.ConnectionString);
        return await context.AuditEntries
            .Where(entry => entry.ActorSubjectId == subject && entry.Action == action)
            .ToListAsync(TestContext.Current.CancellationToken);
    }
}
