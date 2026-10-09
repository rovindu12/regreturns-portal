using System.Buffers.Text;
using System.Net;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Mvc.Testing;

using RegReturns.IntegrationTests.Hosting;
using RegReturns.IntegrationTests.Infrastructure;

namespace RegReturns.IntegrationTests.Web;

/// <summary>
/// The portal's security headers (ADR 0033): a Content Security Policy whose script nonce is fresh on every response
/// and carried by every script the page renders, the fixed headers on pages and static files alike, HSTS for real host
/// names, and no caching of signed-in pages.
/// </summary>
[Collection(HostedAppsDefinition.Name)]
public sealed partial class PortalSecurityHeadersTests : IClassFixture<PortalDatabaseFixture>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SupervisionPortal _supervision;

    public PortalSecurityHeadersTests(PortalDatabaseFixture database)
    {
        _factory = PortalHost.Create(database.ConnectionString);
        _supervision = new SupervisionPortal(_factory, database.ConnectionString);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_page_allows_only_scripts_that_carry_its_nonce()
    {
        using var client = Anonymous();

        var response = await client.GetAsync(new Uri("/", UriKind.Relative), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        var nonce = NonceOf(response);
        Policy(response).ShouldContain($"script-src 'nonce-{nonce}';");
        var scripts = ScriptTag().Matches(html).Select(m => m.Value).ToList();
        scripts.ShouldNotBeEmpty();
        scripts.ShouldAllBe(tag => tag.Contains($"nonce=\"{nonce}\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Every_response_gets_a_new_nonce()
    {
        using var client = Anonymous();

        var first = NonceOf(await client.GetAsync(new Uri("/", UriKind.Relative), Ct));
        var second = NonceOf(await client.GetAsync(new Uri("/", UriKind.Relative), Ct));

        first.ShouldNotBe(second);
        Base64Url.DecodeFromChars(first).Length.ShouldBe(16);
    }

    [Fact]
    public async Task The_policy_loads_everything_else_from_the_portal_and_forbids_framing()
    {
        using var client = Anonymous();

        var policy = Policy(await client.GetAsync(new Uri("/", UriKind.Relative), Ct));

        policy.ShouldStartWith("default-src 'self';");
        policy.ShouldContain("style-src 'self';");
        policy.ShouldContain("object-src 'none';");
        policy.ShouldContain("base-uri 'none';");
        policy.ShouldContain("form-action 'self' https://iam.test.invalid;");
        policy.ShouldContain("frame-ancestors 'none';");
        policy.ShouldNotContain("unsafe-inline");
        policy.ShouldNotContain("unsafe-eval");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/status")]
    [InlineData("/css/site.css")]
    [InlineData("/js/site.js")]
    [InlineData("/health/live")]
    [InlineData("/no-such-page")]
    public async Task Pages_files_and_errors_carry_the_fixed_headers(string path)
    {
        using var client = Anonymous();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        response.Headers.GetValues("Content-Security-Policy").ShouldHaveSingleItem();
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("X-Frame-Options").ShouldBe(["DENY"]);
        response.Headers.GetValues("Referrer-Policy").ShouldBe(["same-origin"]);
        response.Headers.GetValues("Permissions-Policy").Single().ShouldContain("camera=()");
        response.Headers.GetValues("Cross-Origin-Opener-Policy").ShouldBe(["same-origin"]);
        response.Headers.GetValues("Cross-Origin-Resource-Policy").ShouldBe(["same-origin"]);
        response.Headers.Contains("Server").ShouldBeFalse();
        response.Headers.Contains("X-Powered-By").ShouldBeFalse();
    }

    [Fact]
    public async Task A_real_host_name_is_told_to_use_https_for_a_year()
    {
        using var client = Anonymous(new Uri("https://portal.example"));

        var response = await client.GetAsync(new Uri("/", UriKind.Relative), Ct);

        response.Headers.GetValues("Strict-Transport-Security").ShouldBe(["max-age=31536000; includeSubDomains"]);
    }

    [Fact]
    public async Task Localhost_is_not_pinned_to_https()
    {
        using var client = Anonymous();

        var response = await client.GetAsync(new Uri("/", UriKind.Relative), Ct);

        response.Headers.Contains("Strict-Transport-Security").ShouldBeFalse();
    }

    [Fact]
    public async Task Signed_in_pages_are_never_stored()
    {
        using var reviewer = await _supervision.ReviewerAsync();

        var response = await reviewer.GetAsync(new Uri("/reports", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl.ShouldNotBeNull().NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task Static_files_stay_cacheable_for_signed_in_users()
    {
        using var reviewer = await _supervision.ReviewerAsync();

        var response = await reviewer.GetAsync(new Uri("/css/site.css", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (response.Headers.CacheControl?.NoStore ?? false).ShouldBeFalse();
    }

    [Fact]
    public async Task A_signed_in_page_with_charts_gives_its_scripts_the_nonce()
    {
        using var reviewer = await _supervision.ReviewerAsync();

        var response = await reviewer.GetAsync(new Uri("/reports", UriKind.Relative), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        var nonce = NonceOf(response);
        var scripts = ScriptTag().Matches(html).Select(m => m.Value).ToList();
        scripts.ShouldContain(tag => tag.Contains("reports.js", StringComparison.Ordinal));
        scripts.ShouldAllBe(tag => tag.Contains($"nonce=\"{nonce}\"", StringComparison.Ordinal));
        html.ShouldNotContain(" style=\"", Case.Sensitive, "The policy refuses inline style attributes.");
    }

    public void Dispose() => _factory.Dispose();

    private HttpClient Anonymous(Uri? baseAddress = null)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.BaseAddress = baseAddress ?? PortalHost.BaseAddress;
        return client;
    }

    private static string Policy(HttpResponseMessage response) =>
        response.Headers.GetValues("Content-Security-Policy").ShouldHaveSingleItem();

    private static string NonceOf(HttpResponseMessage response)
    {
        var match = NoncePattern().Match(Policy(response));
        match.Success.ShouldBeTrue("The policy has no script nonce.");
        return match.Groups[1].Value;
    }

    [GeneratedRegex("<script\\b[^>]*>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ScriptTag();

    [GeneratedRegex("'nonce-([A-Za-z0-9_-]+)'", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NoncePattern();
}
