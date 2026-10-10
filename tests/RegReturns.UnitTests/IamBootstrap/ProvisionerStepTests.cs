extern alias IamBootstrapTool;

using System.Net;

using IamBootstrapTool::RegReturns.IamBootstrap;
using IamBootstrapTool::RegReturns.IamBootstrap.Steps;
using IamBootstrapTool::RegReturns.IamBootstrap.Wso2;

using RegReturns.Infrastructure.Identity.Wso2;

namespace RegReturns.UnitTests.IamBootstrap;

public sealed class ProvisionerStepTests : IDisposable
{
    private const string Apps = "api/server/v1/applications";
    private const string Resources = "api/server/v1/api-resources";
    private const string AuthorizedApis = $"{Apps}/prov-1/authorized-apis";

    private static readonly string[] UserScopes = ["internal_user_mgt_list", "internal_user_mgt_view", "internal_user_mgt_update"];

    private readonly StubWso2 _wso2 = new();

    [Fact]
    public async Task New_provisioner_is_a_client_credentials_app()
    {
        Wso2HasTheSystemApis();

        await RunAsync();

        var create = _wso2.Writes().First(r => r.PathAndQuery == Apps).Body!;
        create["name"]!.GetValue<string>().ShouldBe(IamNames.ProvisionerApp);
        create["templateId"]!.GetValue<string>().ShouldBe("m2m-application");
        var oidc = create["inboundProtocolConfiguration"]!["oidc"]!;
        oidc["clientId"]!.GetValue<string>().ShouldBe(IamNames.ProvisionerClientId);
        oidc["grantTypes"]!.AsArray().Select(g => g!.GetValue<string>()).ShouldBe(["client_credentials"]);
    }

    [Fact]
    public async Task System_apis_are_resolved_by_identifier()
    {
        Wso2HasTheSystemApis();

        await RunAsync();

        _wso2.Requests.Select(r => r.PathAndQuery).Where(p => p.StartsWith(Resources, StringComparison.Ordinal)).ShouldBe(
        [
            $"{Resources}?filter=identifier+eq+%2Fscim2%2FUsers",
            $"{Resources}?filter=identifier+eq+%2Fscim2%2FRoles",
        ]);
    }

    [Fact]
    public async Task Users_api_gets_exactly_the_scopes_the_portal_requests()
    {
        Wso2HasTheSystemApis();

        await RunAsync();

        AuthorizedScopes()["sys-users"].ShouldBe(UserScopes, ignoreOrder: true);
        ProvisionerStep.UserScopes.ShouldBe(Wso2IdentityDirectory.Scopes.Split(' '), ignoreOrder: true);
    }

    [Fact]
    public async Task Roles_api_is_not_authorized()
    {
        Wso2HasTheSystemApis();

        await RunAsync();

        AuthorizedScopes().Keys.ShouldBe(["sys-users"]);
    }

    [Fact]
    public async Task A_roles_authorization_from_an_earlier_version_is_withdrawn()
    {
        Wso2HasTheSystemApis(authorized: """[{"id":"sys-roles"}]""");
        _wso2.On(HttpMethod.Delete, $"{AuthorizedApis}/sys-roles", HttpStatusCode.NoContent);

        var state = await RunAsync();

        _wso2.Writes().ShouldContain(r => r.Method == HttpMethod.Delete && r.PathAndQuery == $"{AuthorizedApis}/sys-roles");
        state.Changes.ShouldContain(("withdrawn API", $"{IamNames.ProvisionerApp} /scim2/Roles", Outcome.Updated));
    }

    [Fact]
    public async Task Provisioner_credentials_are_written_to_the_generated_settings()
    {
        Wso2HasTheSystemApis();

        var state = await RunAsync();

        state.GeneratedSettings[ProvisionerStep.ClientIdKey].ShouldBe(IamNames.ProvisionerClientId);
        state.GeneratedSettings[ProvisionerStep.ClientSecretKey].ShouldBe("prov-secret");
    }

    [Fact]
    public async Task App_and_its_users_authorization_are_recorded()
    {
        Wso2HasTheSystemApis();

        var state = await RunAsync();

        state.Changes.Select(c => (c.Kind, c.Name, c.Outcome)).ShouldBe(
        [
            ("M2M app", IamNames.ProvisionerApp, Outcome.Created),
            ("authorized API", $"{IamNames.ProvisionerApp} /scim2/Users", Outcome.Created),
        ]);
    }

    [Fact]
    public async Task Missing_system_api_stops_the_step()
    {
        AppCanBeCreated();
        _wso2.OnJson(HttpMethod.Get, $"{Resources}?filter=identifier+eq+%2Fscim2%2FUsers", """{"apiResources":[]}""");

        var failure = await Should.ThrowAsync<InvalidOperationException>(RunAsync);

        failure.Message.ShouldContain("/scim2/Users");
    }

    public void Dispose() => _wso2.Dispose();

    private void AppCanBeCreated() =>
        _wso2.OnJson(HttpMethod.Get, Apps, """{"applications":[]}""")
            .On(HttpMethod.Post, Apps, HttpStatusCode.Created, location: $"{StubWso2.Authority}{Apps}/prov-1")
            .OnJson(HttpMethod.Get, $"{Apps}/prov-1/inbound-protocols/oidc", $$"""{"clientId":"{{IamNames.ProvisionerClientId}}","clientSecret":"prov-secret"}""");

    private void Wso2HasTheSystemApis(string authorized = "[]")
    {
        AppCanBeCreated();
        _wso2.OnJson(HttpMethod.Get, $"{Resources}?filter=identifier+eq+%2Fscim2%2FUsers", """{"apiResources":[{"id":"sys-users"}]}""")
            .OnJson(HttpMethod.Get, $"{Resources}?filter=identifier+eq+%2Fscim2%2FRoles", """{"apiResources":[{"id":"sys-roles"}]}""")
            .OnJson(HttpMethod.Get, AuthorizedApis, authorized)
            .On(HttpMethod.Post, AuthorizedApis, HttpStatusCode.OK);
    }

    private Dictionary<string, List<string>> AuthorizedScopes() =>
        _wso2.Writes()
            .Where(r => r.PathAndQuery == AuthorizedApis)
            .ToDictionary(
                r => r.Body!["id"]!.GetValue<string>(),
                r => r.Body!["scopes"]!.AsArray().Select(s => s!.GetValue<string>()).ToList());

    private async Task<BootstrapState> RunAsync()
    {
        var state = new BootstrapState();
        var client = _wso2.Client();
        await new ProvisionerStep(client, new Wso2Applications(client)).RunAsync(state, TestContext.Current.CancellationToken);
        return state;
    }
}
