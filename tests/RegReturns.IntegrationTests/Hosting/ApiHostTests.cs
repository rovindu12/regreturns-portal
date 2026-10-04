extern alias ApiHost;

using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using RegReturns.IntegrationTests.Infrastructure;
using RegReturns.ServiceDefaults.Web;

namespace RegReturns.IntegrationTests.Hosting;

[Collection(HostedAppsDefinition.Name)]
public sealed class ApiHostTests(SqlServerFixture sql) : IDisposable
{
    private readonly WebApplicationFactory<ApiHost::Program> _factory = new WebApplicationFactory<ApiHost::Program>()
        .WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing")
            .UseSetting("Serilog:MinimumLevel:Default", "Warning")
            .UseSetting("ConnectionStrings:RegReturns", sql.ConnectionString));

    [Fact]
    public async Task Readiness_reports_healthy()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(WebDefaultsExtensions.ReadyPath, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unknown_routes_return_problem_details_with_a_trace_id()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/v1/does-not-exist", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        json.RootElement.GetProperty("traceId").GetString()
            .ShouldBe(response.Headers.GetValues(WebDefaultsExtensions.TraceIdHeader).Single());
    }

    public void Dispose() => _factory.Dispose();
}
