using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;

using RegReturns.IntegrationTests.Hosting;
using RegReturns.IntegrationTests.Infrastructure;

namespace RegReturns.IntegrationTests.Web;

/// <summary>
/// The administrator's diagnostics page (ADR 0033): health checks with timings, the database's migrations and its
/// encrypted connection, the audit chain's head and the settings, with secrets reported only as set.
/// </summary>
[Collection(HostedAppsDefinition.Name)]
public sealed class AdminDiagnosticsTests : IClassFixture<PortalDatabaseFixture>, IDisposable
{
    private const string ProvisionerSecret = "it-provisioner-secret";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly SupervisionPortal _supervision;

    public AdminDiagnosticsTests(PortalDatabaseFixture database)
    {
        _factory = PortalHost.Create(
            database.ConnectionString,
            settings: new Dictionary<string, string?>
            {
                ["Iam:Provisioner:ClientId"] = "it-provisioner",
                ["Iam:Provisioner:ClientSecret"] = ProvisionerSecret,
            });
        _supervision = new SupervisionPortal(_factory, database.ConnectionString);
    }

    [Fact]
    public async Task The_page_shows_the_checks_the_database_and_the_build()
    {
        using var admin = await _supervision.AdminAsync();

        var page = await PortalForms.GetTextAsync(admin, "/admin/diagnostics");

        page.ShouldContain("data-check=\"database\"");
        page.ShouldContain("data-check=\"self\"");
        page.ShouldContain("latest <code>");
        page.ShouldNotContain("Pending:");
        page.ShouldContain("Audit chain");
        page.ShouldContain("Testing");
    }

    [Fact]
    public async Task The_database_connection_is_shown_encrypted_with_a_pinned_certificate()
    {
        using var admin = await _supervision.AdminAsync();

        var page = await PortalForms.GetTextAsync(admin, "/admin/diagnostics");

        page.ShouldContain("encrypted, TDS 8 · Encrypt=Strict, certificate pinned");
    }

    [Fact]
    public async Task Secrets_are_reported_only_as_set()
    {
        using var admin = await _supervision.AdminAsync();

        var page = await PortalForms.GetTextAsync(admin, "/admin/diagnostics");

        page.ShouldContain("client id and secret set");
        page.ShouldNotContain(ProvisionerSecret);
        page.ShouldNotContain(TestAuth.AuditKey);
        page.ShouldNotContain("test-only-client-secret");
        page.ShouldNotContain("Password=", Case.Insensitive);
    }

    [Fact]
    public async Task Only_administrators_see_it()
    {
        using var reviewer = await _supervision.ReviewerAsync();

        var response = await reviewer.GetAsync(new Uri("/admin/diagnostics", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    public void Dispose() => _factory.Dispose();
}
