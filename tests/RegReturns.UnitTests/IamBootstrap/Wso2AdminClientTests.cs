extern alias IamBootstrapTool;

using System.Net;
using System.Text;
using System.Text.Json.Nodes;

using IamBootstrapTool::RegReturns.IamBootstrap.Wso2;

namespace RegReturns.UnitTests.IamBootstrap;

public sealed class Wso2AdminClientTests : IDisposable
{
    private readonly StubWso2 _wso2 = new();

    [Fact]
    public void Basic_credentials_encode_user_and_password_as_utf8()
    {
        var header = Wso2AdminClient.BasicCredentials("iamadmin", "pässword");

        header.Scheme.ShouldBe("Basic");
        Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter!)).ShouldBe("iamadmin:pässword");
    }

    [Fact]
    public async Task Request_goes_to_the_path_under_the_authority()
    {
        _wso2.On(HttpMethod.Get, "api/server/v1/applications", HttpStatusCode.OK, new JsonObject());

        await _wso2.Client().SendAsync(HttpMethod.Get, "api/server/v1/applications?filter=name+eq+x", null, Wso2AdminClient.Json, TestContext.Current.CancellationToken);

        _wso2.Requests.ShouldHaveSingleItem().PathAndQuery.ShouldBe("api/server/v1/applications?filter=name+eq+x");
    }

    [Fact]
    public async Task Body_is_sent_as_json_with_the_requested_media_type()
    {
        _wso2.On(HttpMethod.Post, "scim2/Users", HttpStatusCode.Created, new JsonObject { ["id"] = "u1" });

        await _wso2.Client().SendAsync(
            HttpMethod.Post, "scim2/Users", new JsonObject { ["userName"] = "maker.alpha" }, Wso2AdminClient.ScimJson, TestContext.Current.CancellationToken);

        var request = _wso2.Requests.ShouldHaveSingleItem();
        request.ContentType.ShouldBe(Wso2AdminClient.ScimJson);
        request.Accept.ShouldBe(Wso2AdminClient.ScimJson);
        request.Body!["userName"]!.GetValue<string>().ShouldBe("maker.alpha");
    }

    [Fact]
    public async Task Response_names_the_path_without_the_query_string()
    {
        _wso2.On(HttpMethod.Get, "scim2/Users", HttpStatusCode.OK, new JsonObject());

        var response = await _wso2.Client().SendAsync(
            HttpMethod.Get, "scim2/Users?filter=userName+eq+secret.name", null, Wso2AdminClient.ScimJson, TestContext.Current.CancellationToken);

        response.Path.ShouldBe("scim2/Users");
        response.Method.ShouldBe("GET");
    }

    [Fact]
    public async Task Json_body_and_location_are_returned()
    {
        _wso2.On(HttpMethod.Post, "api/server/v1/applications", HttpStatusCode.Created, new JsonObject { ["ok"] = true },
            location: "https://iam.valoria.test/api/server/v1/applications/app-1");

        var response = await _wso2.Client().SendAsync(
            HttpMethod.Post, "api/server/v1/applications", new JsonObject(), Wso2AdminClient.Json, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Body!["ok"]!.GetValue<bool>().ShouldBeTrue();
        response.LocationId.ShouldBe("app-1");
    }

    [Fact]
    public async Task GetOrDefault_returns_null_for_a_missing_resource()
    {
        _wso2.On(HttpMethod.Get, "api/server/v1/oidc/scopes/institution", HttpStatusCode.NotFound, new JsonObject { ["code"] = "OIDC-50001" });

        (await _wso2.Client().GetOrDefaultAsync("api/server/v1/oidc/scopes/institution", TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task GetOrDefault_throws_for_a_server_error()
    {
        _wso2.On(HttpMethod.Get, "api/server/v1/oidc/scopes/institution", HttpStatusCode.InternalServerError);

        await Should.ThrowAsync<Wso2ApiException>(() =>
            _wso2.Client().GetOrDefaultAsync("api/server/v1/oidc/scopes/institution", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Get_throws_when_a_required_resource_is_missing()
    {
        _wso2.On(HttpMethod.Get, "api/server/v1/applications/app-1", HttpStatusCode.NotFound);

        var failure = await Should.ThrowAsync<Wso2ApiException>(() =>
            _wso2.Client().GetAsync("api/server/v1/applications/app-1", TestContext.Current.CancellationToken));

        failure.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_throws_when_the_body_is_empty()
    {
        _wso2.On(HttpMethod.Get, "api/server/v1/applications/app-1", HttpStatusCode.NoContent);

        await Should.ThrowAsync<Wso2ApiException>(() =>
            _wso2.Client().GetAsync("api/server/v1/applications/app-1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Html_error_pages_are_not_parsed_as_json()
    {
        var handler = new HtmlHandler();
        var client = new Wso2AdminClient(
            new HttpClient(handler) { BaseAddress = StubWso2.Authority },
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Wso2AdminClient>.Instance);

        var response = await client.SendAsync(HttpMethod.Get, "api/server/v1/applications", null, Wso2AdminClient.Json, TestContext.Current.CancellationToken);

        response.Body.ShouldBeNull();
        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
    }

    private sealed class HtmlHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("<html><body>Bad gateway</body></html>", Encoding.UTF8, "text/html"),
            });
    }

    public void Dispose() => _wso2.Dispose();
}
