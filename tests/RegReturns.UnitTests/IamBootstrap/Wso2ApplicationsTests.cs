extern alias IamBootstrapTool;

using System.Net;
using System.Text.Json.Nodes;

using IamBootstrapTool::RegReturns.IamBootstrap.Steps;
using IamBootstrapTool::RegReturns.IamBootstrap.Wso2;

namespace RegReturns.UnitTests.IamBootstrap;

public sealed class Wso2ApplicationsTests : IDisposable
{
    private const string Apps = "api/server/v1/applications";
    private const string Lookup = $"{Apps}?filter=name+eq+RegReturns%20Bank%20HLB&attributes=clientId,applicationEnabled";
    private const string Name = "RegReturns Bank HLB";

    private readonly StubWso2 _wso2 = new();

    [Fact]
    public async Task Missing_application_is_created_with_its_settings_and_a_generated_secret()
    {
        _wso2.OnJson(HttpMethod.Get, Lookup, """{"applications":[]}""")
            .On(HttpMethod.Post, Apps, HttpStatusCode.Created, location: $"https://iam.valoria.test/{Apps}/app-1")
            .OnJson(HttpMethod.Get, $"{Apps}/app-1/inbound-protocols/oidc", """{"clientId":"regreturns-bank-hlb","clientSecret":"stored-secret"}""");

        var app = await EnsureAsync();

        var create = _wso2.Writes().ShouldHaveSingleItem().Body!;
        create["name"]!.GetValue<string>().ShouldBe(Name);
        create["description"]!.GetValue<string>().ShouldBe("Bank system");
        create["templateId"]!.GetValue<string>().ShouldBe("m2m-application");
        var oidc = create["inboundProtocolConfiguration"]!["oidc"]!;
        oidc["clientId"]!.GetValue<string>().ShouldBe("regreturns-bank-hlb");
        oidc["clientSecret"]!.GetValue<string>().Length.ShouldBeGreaterThanOrEqualTo(43);
        oidc["grantTypes"]![0]!.GetValue<string>().ShouldBe("client_credentials");
        app.ShouldBe(new Wso2Application("app-1", "regreturns-bank-hlb", "stored-secret", Outcome.Created));
    }

    [Fact]
    public async Task Creation_without_a_location_header_fails()
    {
        _wso2.OnJson(HttpMethod.Get, Lookup, """{"applications":[]}""")
            .On(HttpMethod.Post, Apps, HttpStatusCode.Created);

        await Should.ThrowAsync<Wso2ApiException>(EnsureAsync);
    }

    [Fact]
    public async Task Matching_application_is_left_unchanged()
    {
        Existing(
            oidc: """{"clientId":"existing-id","clientSecret":"kept","grantTypes":["client_credentials"],"publicClient":false,"extra":1}""",
            app: """{"id":"app-1","name":"RegReturns Bank HLB","description":"Bank system","templateId":"m2m-application"}""");

        var app = await EnsureAsync();

        _wso2.Writes().ShouldBeEmpty();
        app.ShouldBe(new Wso2Application("app-1", "existing-id", "kept", Outcome.Unchanged));
    }

    [Fact]
    public async Task Drifted_oidc_settings_are_replaced_keeping_the_client_id_and_secret()
    {
        Existing(
            oidc: """{"clientId":"existing-id","clientSecret":"kept","grantTypes":["client_credentials","password"],"publicClient":false}""",
            app: """{"id":"app-1","description":"Bank system"}""");
        _wso2.On(HttpMethod.Put, $"{Apps}/app-1/inbound-protocols/oidc", HttpStatusCode.OK);

        var app = await EnsureAsync();

        var put = _wso2.Writes().ShouldHaveSingleItem();
        put.Method.ShouldBe(HttpMethod.Put);
        put.Body!["clientId"]!.GetValue<string>().ShouldBe("existing-id");
        put.Body.AsObject().ContainsKey("clientSecret").ShouldBeFalse();
        app.Outcome.ShouldBe(Outcome.Updated);
        app.ClientSecret.ShouldBe("kept");
    }

    [Fact]
    public async Task Drifted_top_level_settings_are_patched()
    {
        Existing(
            oidc: """{"clientId":"existing-id","clientSecret":"kept","grantTypes":["client_credentials"],"publicClient":false}""",
            app: """{"id":"app-1","description":"Edited in the Console"}""");
        _wso2.On(HttpMethod.Patch, $"{Apps}/app-1", HttpStatusCode.OK);

        var app = await EnsureAsync();

        var patch = _wso2.Writes().ShouldHaveSingleItem();
        patch.Method.ShouldBe(HttpMethod.Patch);
        patch.Body!["description"]!.GetValue<string>().ShouldBe("Bank system");
        patch.Body.AsObject().ContainsKey("templateId").ShouldBeFalse();
        app.Outcome.ShouldBe(Outcome.Updated);
    }

    [Fact]
    public async Task Unreadable_secret_is_regenerated()
    {
        Existing(
            oidc: """{"clientId":"existing-id","grantTypes":["client_credentials"],"publicClient":false}""",
            app: """{"id":"app-1","description":"Bank system"}""");
        _wso2.OnJson(HttpMethod.Post, $"{Apps}/app-1/inbound-protocols/oidc/regenerate-secret", """{"clientSecret":"rotated"}""");

        var app = await EnsureAsync();

        app.ClientSecret.ShouldBe("rotated");
        app.Outcome.ShouldBe(Outcome.Updated);
    }

    [Fact]
    public async Task Find_matches_the_exact_name_case_insensitively()
    {
        _wso2.OnJson(
            HttpMethod.Get,
            Lookup,
            """{"applications":[{"id":"other","name":"RegReturns Bank HLB (old)"},{"id":"app-1","name":"regreturns bank hlb"}]}""");

        var found = await new Wso2Applications(_wso2.Client()).FindAsync(Name, TestContext.Current.CancellationToken);

        found!["id"]!.GetValue<string>().ShouldBe("app-1");
    }

    [Fact]
    public async Task Find_returns_nothing_when_only_similar_names_exist()
    {
        _wso2.OnJson(HttpMethod.Get, Lookup, """{"applications":[{"id":"other","name":"RegReturns Bank HLB (old)"}]}""");

        (await new Wso2Applications(_wso2.Client()).FindAsync(Name, TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task Unauthorized_api_is_authorized_with_the_scopes()
    {
        _wso2.OnJson(HttpMethod.Get, $"{Apps}/app-1/authorized-apis", "[]")
            .On(HttpMethod.Post, $"{Apps}/app-1/authorized-apis", HttpStatusCode.OK);

        var outcome = await AuthorizeAsync("returns:read", "returns:submit");

        outcome.ShouldBe(Outcome.Created);
        var body = _wso2.Writes().ShouldHaveSingleItem().Body!;
        body["id"]!.GetValue<string>().ShouldBe("res-1");
        body["policyIdentifier"]!.GetValue<string>().ShouldBe("RBAC");
        body["scopes"]!.AsArray().Select(s => s!.GetValue<string>()).ShouldBe(["returns:read", "returns:submit"]);
    }

    [Fact]
    public async Task Authorized_api_with_the_same_scopes_is_left_unchanged()
    {
        _wso2.OnJson(HttpMethod.Get, $"{Apps}/app-1/authorized-apis", Authorized("returns:submit", "returns:read"));

        (await AuthorizeAsync("returns:read", "returns:submit")).ShouldBe(Outcome.Unchanged);
        _wso2.Writes().ShouldBeEmpty();
    }

    [Fact]
    public async Task Authorized_scopes_are_brought_in_line_by_adding_and_removing()
    {
        _wso2.OnJson(HttpMethod.Get, $"{Apps}/app-1/authorized-apis", Authorized("returns:read", "returns:submit"))
            .On(HttpMethod.Patch, $"{Apps}/app-1/authorized-apis/res-1", HttpStatusCode.OK);

        var outcome = await AuthorizeAsync("returns:read", "reference:read");

        outcome.ShouldBe(Outcome.Updated);
        var body = _wso2.Writes().ShouldHaveSingleItem().Body!;
        body["addedScopes"]!.AsArray().Select(s => s!.GetValue<string>()).ShouldBe(["reference:read"]);
        body["removedScopes"]!.AsArray().Select(s => s!.GetValue<string>()).ShouldBe(["returns:submit"]);
    }

    [Fact]
    public void New_secrets_are_url_safe_and_random()
    {
        var first = Wso2Applications.NewSecret();

        first.Length.ShouldBe(43);
        first.ShouldMatch("^[A-Za-z0-9_-]+$");
        Wso2Applications.NewSecret().ShouldNotBe(first);
    }

    private void Existing(string oidc, string app) =>
        _wso2.OnJson(HttpMethod.Get, Lookup, """{"applications":[{"id":"app-1","name":"RegReturns Bank HLB"}]}""")
            .OnJson(HttpMethod.Get, $"{Apps}/app-1/inbound-protocols/oidc", oidc)
            .OnJson(HttpMethod.Get, $"{Apps}/app-1", app);

    private async Task<Wso2Application> EnsureAsync()
    {
        var settings = new JsonObject { ["description"] = "Bank system" };
        var oidc = new JsonObject { ["grantTypes"] = new JsonArray("client_credentials"), ["publicClient"] = false };
        var createOnly = new JsonObject { ["templateId"] = "m2m-application" };
        return await new Wso2Applications(_wso2.Client())
            .EnsureAsync(Name, "regreturns-bank-hlb", settings, oidc, createOnly, TestContext.Current.CancellationToken);
    }

    private Task<Outcome> AuthorizeAsync(params string[] scopes) =>
        new Wso2Applications(_wso2.Client()).EnsureAuthorizedApiAsync("app-1", "res-1", scopes, TestContext.Current.CancellationToken);

    private static string Authorized(params string[] scopes) =>
        new JsonArray(new JsonObject
        {
            ["id"] = "res-1",
            ["authorizedScopes"] = new JsonArray([.. scopes.Select(s => new JsonObject { ["name"] = s })]),
        }).ToJsonString();

    public void Dispose() => _wso2.Dispose();
}
