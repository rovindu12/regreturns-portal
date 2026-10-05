extern alias IamBootstrapTool;

using System.Net;
using System.Text.Json.Nodes;

using IamBootstrapTool::RegReturns.IamBootstrap;
using IamBootstrapTool::RegReturns.IamBootstrap.Steps;
using IamBootstrapTool::RegReturns.IamBootstrap.Wso2;

using RegReturns.Application.Identity;

namespace RegReturns.UnitTests.IamBootstrap;

public sealed class ApiResourceStepTests : IDisposable
{
    private const string Resources = "api/server/v1/api-resources";
    private const string Lookup = $"{Resources}?filter=identifier+eq+https%3A%2F%2Fapi.regreturns";

    private readonly StubWso2 _wso2 = new();

    [Fact]
    public async Task Missing_resource_is_created_with_every_scope()
    {
        _wso2.OnJson(HttpMethod.Get, Lookup, """{"apiResources":[]}""")
            .On(HttpMethod.Post, Resources, HttpStatusCode.Created, new JsonObject { ["id"] = "res-1" });

        var state = await RunAsync();

        var create = _wso2.Writes().ShouldHaveSingleItem();
        create.Method.ShouldBe(HttpMethod.Post);
        create.Body!["identifier"]!.GetValue<string>().ShouldBe(ApiScopes.ApiIdentifier);
        create.Body["name"]!.GetValue<string>().ShouldBe(IamNames.ApiResourceName);
        create.Body["requiresAuthorization"]!.GetValue<bool>().ShouldBeTrue();
        ScopeNames(create.Body["scopes"]).ShouldBe(ApiScopes.All, ignoreOrder: true);
        state.ApiResourceId.ShouldBe("res-1");
        OutcomeOf(state).ShouldBe(Outcome.Created);
    }

    [Fact]
    public async Task Created_resource_id_falls_back_to_the_location_header()
    {
        _wso2.OnJson(HttpMethod.Get, Lookup, """{"apiResources":[]}""")
            .On(HttpMethod.Post, Resources, HttpStatusCode.Created, location: $"https://iam.valoria.test/{Resources}/res-2");

        (await RunAsync()).ApiResourceId.ShouldBe("res-2");
    }

    [Fact]
    public async Task Existing_resource_with_every_scope_is_left_unchanged()
    {
        _wso2.OnJson(HttpMethod.Get, Lookup, """{"apiResources":[{"id":"res-1"}]}""")
            .OnJson(HttpMethod.Get, $"{Resources}/res-1/scopes", ScopesJson(ApiScopes.All));

        var state = await RunAsync();

        _wso2.Writes().ShouldBeEmpty();
        state.ApiResourceId.ShouldBe("res-1");
        OutcomeOf(state).ShouldBe(Outcome.Unchanged);
    }

    [Fact]
    public async Task Only_missing_scopes_are_added()
    {
        _wso2.OnJson(HttpMethod.Get, Lookup, """{"apiResources":[{"id":"res-1"}]}""")
            .OnJson(HttpMethod.Get, $"{Resources}/res-1/scopes", ScopesJson([ApiScopes.ReturnsRead, ApiScopes.ReferenceRead]))
            .On(HttpMethod.Patch, $"{Resources}/res-1", HttpStatusCode.NoContent);

        var state = await RunAsync();

        var patch = _wso2.Writes().ShouldHaveSingleItem();
        patch.Method.ShouldBe(HttpMethod.Patch);
        ScopeNames(patch.Body!["addedScopes"]).ShouldBe([ApiScopes.ReturnsSubmit]);
        patch.Body.AsObject().Select(p => p.Key).ShouldBe(["addedScopes"]);
        OutcomeOf(state).ShouldBe(Outcome.Updated);
    }

    [Fact]
    public async Task Scopes_added_by_hand_are_left_alone()
    {
        _wso2.OnJson(HttpMethod.Get, Lookup, """{"apiResources":[{"id":"res-1"}]}""")
            .OnJson(HttpMethod.Get, $"{Resources}/res-1/scopes", ScopesJson([.. ApiScopes.All, "returns:audit"]));

        var state = await RunAsync();

        _wso2.Writes().ShouldBeEmpty();
        OutcomeOf(state).ShouldBe(Outcome.Unchanged);
    }

    [Fact]
    public async Task Failed_creation_stops_the_step()
    {
        _wso2.OnJson(HttpMethod.Get, Lookup, """{"apiResources":[]}""")
            .On(HttpMethod.Post, Resources, HttpStatusCode.Conflict, new JsonObject { ["code"] = "APR-60006", ["description"] = "Exists" });

        var failure = await Should.ThrowAsync<Wso2ApiException>(RunAsync);

        failure.ErrorCode.ShouldBe("APR-60006");
    }

    private async Task<BootstrapState> RunAsync()
    {
        var state = new BootstrapState();
        await new ApiResourceStep(_wso2.Client()).RunAsync(state, TestContext.Current.CancellationToken);
        return state;
    }

    private static Outcome OutcomeOf(BootstrapState state)
    {
        var change = state.Changes.ShouldHaveSingleItem();
        change.Kind.ShouldBe("API resource");
        change.Name.ShouldBe(ApiScopes.ApiIdentifier);
        return change.Outcome;
    }

    private static string ScopesJson(IEnumerable<string> names) =>
        new JsonArray([.. names.Select(n => new JsonObject { ["name"] = n, ["displayName"] = n })]).ToJsonString();

    private static List<string> ScopeNames(JsonNode? scopes) =>
        [.. scopes!.AsArray().Select(s => s!["name"]!.GetValue<string>())];

    public void Dispose() => _wso2.Dispose();
}
