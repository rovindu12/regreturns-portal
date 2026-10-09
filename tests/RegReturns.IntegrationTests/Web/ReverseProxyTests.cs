using System.Net;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using RegReturns.Domain.Auditing;
using RegReturns.IntegrationTests.Hosting;
using RegReturns.IntegrationTests.Infrastructure;
using RegReturns.ServiceDefaults.Web;
using RegReturns.Web.Navigation;

namespace RegReturns.IntegrationTests.Web;

/// <summary>
/// The portal behind Caddy (ADR 0034): forwarded headers are believed only from the proxy's network, the edge does
/// the HTTPS redirect, and the data-protection keys survive a restart when they live on a volume.
/// </summary>
[Collection(HostedAppsDefinition.Name)]
public sealed class ReverseProxyTests(PortalDatabaseFixture database) : IClassFixture<PortalDatabaseFixture>
{
    private const string ProxyNetwork = "10.20.30.0/24";
    private const string ProxyAddress = "10.20.30.4";
    private const string ClientAddress = "203.0.113.9";

    // TestServer has no TCP peer; this test-only header stands in for the address the request arrived from.
    private const string PeerHeader = "X-Test-Peer";

    private static readonly Uri PublicAddress = new("http://portal.example");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Https_forwarded_by_the_proxy_counts_as_https()
    {
        await using var factory = Host(behindProxy: true);
        using var client = Client(factory, ProxyAddress);
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        var response = await client.GetAsync(new Uri("/", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("Strict-Transport-Security").ShouldBeTrue("HSTS is only sent over HTTPS.");
    }

    [Fact]
    public async Task Forwarded_headers_from_outside_the_proxy_network_are_ignored()
    {
        await using var factory = Host(behindProxy: true);
        using var client = Client(factory, "192.0.2.10");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        var response = await client.GetAsync(new Uri("/", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("Strict-Transport-Security").ShouldBeFalse();
    }

    [Fact]
    public async Task The_audit_trail_records_the_client_address_the_proxy_forwards()
    {
        await using var factory = Host(behindProxy: true);
        var portal = new SupervisionPortal(factory, database.ConnectionString);
        using var reviewer = await portal.ReviewerAsync();
        reviewer.BaseAddress = PublicAddress;
        reviewer.DefaultRequestHeaders.Add(PeerHeader, ProxyAddress);
        reviewer.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        reviewer.DefaultRequestHeaders.Add("X-Forwarded-For", ClientAddress);

        await reviewer.GetAsync(new Uri(PortalAreas.Admin.Path, UriKind.Relative), Ct);

        await using var context = SqlServerFixture.CreateContext(database.ConnectionString);
        var denied = await context.AuditEntries.Where(e => e.Action == AuditAction.AccessDenied).ToListAsync(Ct);
        denied.ShouldHaveSingleItem().IpAddress.ShouldBe(ClientAddress);
    }

    [Fact]
    public async Task Behind_the_proxy_plain_http_on_the_internal_network_is_not_redirected()
    {
        await using var factory = Host(behindProxy: true);
        using var client = Client(factory, ProxyAddress);

        var response = await client.GetAsync(new Uri(WebDefaultsExtensions.LivePath, UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Without_a_proxy_the_portal_redirects_plain_http_itself()
    {
        await using var factory = Host(behindProxy: false);
        using var client = Client(factory, ProxyAddress);
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        var response = await client.GetAsync(new Uri("/", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.TemporaryRedirect);
        response.Headers.Location.ShouldBe(new Uri("https://portal.example/"));
    }

    [Fact]
    public async Task Keys_on_a_volume_outlive_the_portal_instance_that_made_them()
    {
        var keys = Directory.CreateTempSubdirectory("regreturns-keys-");
        try
        {
            string protectedPayload;
            await using (var first = Host(behindProxy: true, keysPath: keys.FullName))
            {
                protectedPayload = Protector(first).Protect("session ticket");
            }

            keys.GetFiles("key-*.xml").ShouldHaveSingleItem();
            await using var second = Host(behindProxy: true, keysPath: keys.FullName);
            Protector(second).Unprotect(protectedPayload).ShouldBe("session ticket");
        }
        finally
        {
            keys.Delete(recursive: true);
        }
    }

    private static IDataProtector Protector(WebApplicationFactory<Program> factory) =>
        factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("RegReturns.IntegrationTests");

    private static HttpClient Client(WebApplicationFactory<Program> factory, string peer)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = PublicAddress });
        client.DefaultRequestHeaders.Add(PeerHeader, peer);
        return client;
    }

    private WebApplicationFactory<Program> Host(bool behindProxy, string network = ProxyNetwork, string? keysPath = null)
    {
        // https_port lets the HTTPS redirection know where to send plain HTTP, as a real HTTPS endpoint would.
        var settings = new Dictionary<string, string?> { ["https_port"] = "443" };
        if (behindProxy)
        {
            settings["ReverseProxy:KnownNetworks:0"] = network;
        }

        if (keysPath is not null)
        {
            settings["DataProtection:KeysPath"] = keysPath;
        }

        return PortalHost.Create(
            database.ConnectionString,
            services => services.AddSingleton<IStartupFilter, PeerAddressFilter>(),
            settings);
    }

    private sealed class PeerAddressFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (IPAddress.TryParse(context.Request.Headers[PeerHeader].ToString(), out var peer))
                {
                    context.Connection.RemoteIpAddress = peer;
                }

                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
