using System.Collections.Concurrent;
using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using RegReturns.IntegrationTests.Hosting;
using RegReturns.IntegrationTests.Infrastructure;
using RegReturns.ServiceDefaults.Web;
using RegReturns.Web.Status;

namespace RegReturns.IntegrationTests.Web;

/// <summary>
/// The status page lists the REST API when the portal knows where its readiness check is (ADR 0034), without making
/// the portal's own readiness depend on it.
/// </summary>
[Collection(HostedAppsDefinition.Name)]
public sealed class StatusPageTests(SqlServerFixture sql)
{
    private static readonly Uri ApiHealthUrl = new("http://api.internal.test/health/ready");

    private readonly ConcurrentQueue<Uri?> _requests = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(HttpStatusCode.OK, "Operational")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Down")]
    public async Task The_api_is_shown_as_its_readiness_check_answers(HttpStatusCode answer, string level)
    {
        await using var factory = Host(answer);
        using var client = Anonymous(factory);

        var html = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri("/status", UriKind.Relative), Ct));

        html.ShouldMatch($"(?s)data-component=\"REST API\".*?>{level}<");
        _requests.ShouldHaveSingleItem().ShouldBe(ApiHealthUrl);
    }

    [Fact]
    public async Task The_portal_stays_ready_when_the_api_is_down()
    {
        await using var factory = Host(HttpStatusCode.ServiceUnavailable);
        using var client = Anonymous(factory);

        var response = await client.GetAsync(new Uri(WebDefaultsExtensions.ReadyPath, UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_the_api_url_the_page_does_not_list_the_api()
    {
        await using var factory = PortalHost.Create(sql.ConnectionString);
        using var client = Anonymous(factory);

        var html = await client.GetStringAsync(new Uri("/status", UriKind.Relative), Ct);

        html.ShouldNotContain("REST API");
    }

    private WebApplicationFactory<Program> Host(HttpStatusCode answer) => PortalHost.Create(
        sql.ConnectionString,
        services => services.AddHttpClient(ApiHealthCheck.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new StubApi(answer, _requests)),
        new Dictionary<string, string?> { ["Status:ApiHealthUrl"] = ApiHealthUrl.ToString() });

    private static HttpClient Anonymous(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.BaseAddress = PortalHost.BaseAddress;
        return client;
    }

    private sealed class StubApi(HttpStatusCode answer, ConcurrentQueue<Uri?> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Enqueue(request.RequestUri);
            return Task.FromResult(new HttpResponseMessage(answer));
        }
    }
}
