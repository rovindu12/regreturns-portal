using System.Net;

using RegReturns.Application.Identity;
using RegReturns.Infrastructure.Persistence.Seeding;
using RegReturns.IntegrationTests.Hosting;

namespace RegReturns.IntegrationTests.Api;

/// <summary>The public OpenAPI document and Swagger UI (ADR 0027).</summary>
[Collection(HostedAppsDefinition.Name)]
public sealed class ApiDocumentationTests(TestSchemeApiFixture api) : IClassFixture<TestSchemeApiFixture>
{
    [Fact]
    public async Task The_openapi_document_describes_the_versioned_endpoints_without_a_token()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = api.Factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var document = await response.ReadJsonAsync(ct);
        var paths = document.RootElement.GetProperty("paths");
        paths.TryGetProperty("/v1/submissions", out _).ShouldBeTrue();
        paths.TryGetProperty("/v1/return-types/{code}/template", out _).ShouldBeTrue();
        paths.TryGetProperty("/v{version}/submissions", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task The_document_offers_client_credentials_against_the_identity_provider()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = api.Factory.CreateClient();

        using var document = await (await client.GetAsync("/openapi/v1.json", ct)).ReadJsonAsync(ct);

        var flow = document.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("oauth2")
            .GetProperty("flows").GetProperty("clientCredentials");
        flow.GetProperty("tokenUrl").GetString().ShouldBe("https://iam.test.invalid/oauth2/token");
        flow.GetProperty("scopes").EnumerateObject().Select(s => s.Name).ShouldBe(
            [ApiScopes.ReferenceRead, ApiScopes.ReturnsRead, ApiScopes.ReturnsSubmit], ignoreOrder: true);
    }

    [Fact]
    public async Task A_delivery_documents_its_required_idempotency_key_and_scope()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = api.Factory.CreateClient();

        using var document = await (await client.GetAsync("/openapi/v1.json", ct)).ReadJsonAsync(ct);

        var post = document.RootElement.GetProperty("paths").GetProperty("/v1/submissions").GetProperty("post");
        var key = post.GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "Idempotency-Key");
        key.GetProperty("in").GetString().ShouldBe("header");
        key.GetProperty("required").GetBoolean().ShouldBeTrue();
        post.GetProperty("security")[0].GetProperty("oauth2").EnumerateArray().Select(s => s.GetString())
            .ShouldBe([ApiScopes.ReturnsSubmit]);
    }

    [Fact]
    public async Task Swagger_ui_is_served_without_a_token()
    {
        using var client = api.Factory.CreateClient();

        var response = await client.GetAsync("/swagger/index.html", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
    }

    [Fact]
    public async Task Swagger_ui_signs_in_as_the_public_demo_client()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = api.Factory.CreateClient();

        var page = await client.GetStringAsync("/swagger/index.html", ct) + await client.GetStringAsync("/swagger/index.js", ct);

        page.ShouldContain("\"clientId\":\"regreturns-demo-api\"");
    }

    [Fact]
    public async Task Responses_report_the_supported_api_versions()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = api.Factory.ClientAs(await api.Database.RegisterClientAsync(
            DemoBank.Meridian, active: true, ct));

        var response = await client.GetAsync("/v1/me", ct);

        response.Headers.GetValues("api-supported-versions").ShouldBe(["1.0"]);
    }
}
