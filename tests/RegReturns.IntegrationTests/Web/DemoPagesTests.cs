using System.Net;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Mvc.Testing;

using RegReturns.Domain.Identity;
using RegReturns.IntegrationTests.Hosting;
using RegReturns.IntegrationTests.Infrastructure;

namespace RegReturns.IntegrationTests.Web;

/// <summary>
/// The public pages of the demo (ADR 0031) against the shared, read-only database: the landing page, the demo
/// accounts with the published credentials, the guided tour, the status page and the banner, and that a deployment
/// that is not the demo shows none of it.
/// </summary>
[Collection(HostedAppsDefinition.Name)]
public sealed partial class DemoPagesTests(SqlServerFixture sql) : IDisposable
{
    private const string Password = "it-demo-password";
    private const string TotpSecret = "JBSWY3DPEHPK3PXPJBSWY3DP";
    private const string ApiClientId = "regreturns-demo-swagger";
    private const string ApiClientSecret = "it-demo-api-secret";

    private readonly WebApplicationFactory<Program> _demo = PortalHost.Create(
        sql.ConnectionString,
        settings: new Dictionary<string, string?>
        {
            ["Demo:Enabled"] = "true",
            ["Demo:UserPassword"] = Password,
            ["Demo:TotpSecrets:APPROVER_MFA"] = TotpSecret,
            ["Demo:ApiClientId"] = ApiClientId,
            ["Demo:ApiClientSecret"] = ApiClientSecret,
            ["Demo:ApiBaseUrl"] = "https://api.demo.test/",
        });

    private readonly WebApplicationFactory<Program> _notDemo = PortalHost.Create(sql.ConnectionString);

    [Theory]
    [InlineData("/")]
    [InlineData("/demo")]
    [InlineData("/demo/guide")]
    [InlineData("/status")]
    public async Task Public_pages_answer_visitors_who_are_not_signed_in(string path)
    {
        using var client = Anonymous(_demo);

        var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_landing_page_shows_the_features_the_architecture_and_the_roles()
    {
        using var client = Anonymous(_demo);

        var html = await PortalForms.GetTextAsync(client, "/");

        html.ShouldContain("Regulatory returns, from bank to supervisor");
        html.ShouldContain("Try the demo");
        html.ShouldContain("img/architecture.svg");
        html.ShouldContain("Who does what");
        html.ShouldContain("bank_maker");
        html.ShouldContain("https://api.demo.test/swagger");
    }

    [Fact]
    public async Task The_demo_page_lists_the_demo_accounts_with_the_published_credentials()
    {
        using var client = Anonymous(_demo);

        var response = await client.GetAsync(new Uri("/demo", UriKind.Relative), TestContext.Current.CancellationToken);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        foreach (var userName in new[] { "maker.hlb", "checker.hlb", "reviewer", "approver.mfa", "admin.demo", "auditor" })
        {
            html.ShouldContain($"data-demo-user=\"{userName}\"");
            html.ShouldContain($"user={userName}\"");
        }

        html.ShouldContain(Password);
        html.ShouldContain(ApiClientId);
        html.ShouldContain(ApiClientSecret);
        html.ShouldContain("reference:read returns:read");
        DemoUser().Matches(html).Select(m => m.Groups[1].Value)
            .ShouldAllBe(name => !name.StartsWith(AppUser.ApiClientUserNamePrefix) && name != AppUser.MigrationUserName);
    }

    [Fact]
    public async Task Only_the_account_with_a_published_key_gets_a_qr_code()
    {
        using var client = Anonymous(_demo);

        var html = await PortalForms.GetTextAsync(client, "/demo");

        TotpKey().Matches(html).Select(m => m.Groups[1].Value).ShouldBe([TotpSecret]);
        Regex.Count(html, "<svg").ShouldBe(1);
        html.ShouldContain("QR code for the authenticator key of approver.mfa");
    }

    [Fact]
    public async Task Every_page_carries_the_demo_banner_in_demo_mode()
    {
        using var client = Anonymous(_demo);

        (await PortalForms.GetTextAsync(client, "/demo/guide")).ShouldContain("Demo environment");
        (await PortalForms.GetTextAsync(client, "/status")).ShouldContain("Demo environment");
    }

    [Fact]
    public async Task The_status_page_shows_the_database_as_operational()
    {
        using var client = Anonymous(_demo);

        var response = await client.GetAsync(new Uri("/status", UriKind.Relative), TestContext.Current.CancellationToken);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
        html.ShouldContain("All systems operational");
        html.ShouldMatch("(?s)Database.*Operational");
    }

    [Theory]
    [InlineData("/demo")]
    [InlineData("/demo/guide")]
    public async Task Outside_demo_mode_the_demo_pages_do_not_exist(string path)
    {
        using var client = Anonymous(_notDemo);

        var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Outside_demo_mode_there_is_no_banner_and_nothing_points_at_the_demo()
    {
        using var client = Anonymous(_notDemo);

        var home = await PortalForms.GetTextAsync(client, "/");
        var status = await PortalForms.GetTextAsync(client, "/status");

        home.ShouldNotContain("Demo environment");
        home.ShouldNotContain("Try the demo");
        status.ShouldNotContain("Demo environment");
    }

    public void Dispose()
    {
        _demo.Dispose();
        _notDemo.Dispose();
    }

    private static HttpClient Anonymous(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.BaseAddress = PortalHost.BaseAddress;
        return client;
    }

    [GeneratedRegex("data-demo-user=\"([^\"]+)\"", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DemoUser();

    [GeneratedRegex("data-demo=\"totp\">([A-Z2-7]+)<", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TotpKey();
}
