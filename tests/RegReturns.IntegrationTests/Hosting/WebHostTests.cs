using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using RegReturns.IntegrationTests.Infrastructure;
using RegReturns.ServiceDefaults.Web;

namespace RegReturns.IntegrationTests.Hosting;

[Collection(HostedAppsDefinition.Name)]
public sealed class WebHostTests(SqlServerFixture sql) : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing")
            .UseSetting("Serilog:MinimumLevel:Default", "Warning")
            .UseSetting("ConnectionStrings:RegReturns", sql.ConnectionString)
            .UseTestAuth());

    [Fact]
    public async Task Readiness_reports_healthy_database_without_exception_details()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(WebDefaultsExtensions.ReadyPath, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        json.RootElement.GetProperty("status").GetString().ShouldBe("Healthy");
        json.RootElement.GetProperty("checks")[0].GetProperty("name").GetString().ShouldBe("database");
    }

    [Fact]
    public async Task Liveness_does_not_touch_the_database()
    {
        using var client = _factory.WithWebHostBuilder(b => b.UseSetting(
            "ConnectionStrings:RegReturns", "Server=127.0.0.1,1;Database=x;User Id=x;Password=x;Connect Timeout=1")).CreateClient();

        var response = await client.GetAsync(WebDefaultsExtensions.LivePath, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Every_response_carries_a_trace_id_header()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        response.Headers.GetValues(WebDefaultsExtensions.TraceIdHeader).Single().Length.ShouldBe(32);
    }

    [Fact]
    public async Task Home_page_shows_the_seeded_figures_and_no_demo_banner_outside_demo_mode()
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/", TestContext.Current.CancellationToken);

        html.ShouldContain("Licensed banks");
        html.ShouldNotContain("Demo environment");
    }

    [Fact]
    public void Every_service_resolves_as_the_development_environment_checks_it()
    {
        // Development validates every registration when the host is built; the other environments find a gap only when
        // a request first needs the service.
        using var factory = _factory.WithWebHostBuilder(builder => builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateOnBuild = true;
            options.ValidateScopes = true;
        }));

        Should.NotThrow(() => factory.Services);
    }

    public void Dispose() => _factory.Dispose();
}
