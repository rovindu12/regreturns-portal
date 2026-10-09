extern alias ApiHost;

using System.Net;
using System.Text.RegularExpressions;

using ApiHost::RegReturns.Api.Hosting;

using RegReturns.IntegrationTests.Hosting;

namespace RegReturns.IntegrationTests.Api;

/// <summary>
/// The API's security headers (ADR 0033): JSON answers, problems included, allow no content of any kind; Swagger UI
/// runs only its own files and may fetch tokens from WSO2.
/// </summary>
[Collection(HostedAppsDefinition.Name)]
public sealed partial class ApiSecurityHeadersTests(TestSchemeApiFixture api) : IClassFixture<TestSchemeApiFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/openapi/v1.json", HttpStatusCode.OK)]
    [InlineData("/v1/institutions", HttpStatusCode.Unauthorized)]
    [InlineData("/health/live", HttpStatusCode.OK)]
    public async Task Answers_carry_the_api_policy_and_the_fixed_headers(string path, HttpStatusCode status)
    {
        using var client = api.Factory.CreateClient();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(status);
        response.Headers.GetValues("Content-Security-Policy").ShouldBe([ApiSecurityHeaders.ApiPolicy]);
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("X-Frame-Options").ShouldBe(["DENY"]);
        response.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"]);
        response.Headers.GetValues("Cross-Origin-Resource-Policy").ShouldBe(["same-origin"]);
        response.Headers.Contains("Server").ShouldBeFalse();
    }

    [Fact]
    public async Task Swagger_ui_gets_its_own_policy_and_has_no_inline_scripts()
    {
        using var client = api.Factory.CreateClient();

        var response = await client.GetAsync(new Uri("/swagger/index.html", UriKind.Relative), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("Content-Security-Policy").ShouldBe([ApiSecurityHeaders.SwaggerPolicy("https://iam.test.invalid")]);
        InlineScript().IsMatch(html).ShouldBeFalse();
    }

    [Fact]
    public async Task A_real_host_name_is_told_to_use_https_for_a_year()
    {
        using var client = api.Factory.CreateClient();
        client.BaseAddress = new Uri("https://api.example");

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative), Ct);

        response.Headers.GetValues("Strict-Transport-Security").ShouldBe(["max-age=31536000; includeSubDomains"]);
    }

    [GeneratedRegex("<script(?![^>]*\\bsrc=)[^>]*>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex InlineScript();
}
