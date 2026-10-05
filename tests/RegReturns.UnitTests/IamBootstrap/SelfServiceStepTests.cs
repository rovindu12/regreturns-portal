extern alias IamBootstrapTool;

using System.Net;
using System.Text.Json.Nodes;

using IamBootstrapTool::RegReturns.IamBootstrap;
using IamBootstrapTool::RegReturns.IamBootstrap.Steps;
using IamBootstrapTool::RegReturns.IamBootstrap.Wso2;

using Microsoft.Extensions.Options;

namespace RegReturns.UnitTests.IamBootstrap;

public sealed class SelfServiceStepTests : IDisposable
{
    private const string Onboarding = "User Onboarding";
    private const string AccountManagement = "Account Management";
    private const string MyAccount = "api/server/v1/applications/my-account-1";

    private readonly StubWso2 _wso2 = new();

    [Fact]
    public async Task Nothing_is_touched_when_self_service_stays_open()
    {
        var state = await RunAsync(lockDown: false);

        _wso2.Requests.ShouldBeEmpty();
        state.Changes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Connectors_already_switched_off_are_left_unchanged()
    {
        AllConnectorsOff();
        MyAccountIs(enabled: false);

        var state = await RunAsync();

        _wso2.Writes().ShouldBeEmpty();
        state.Changes.Where(c => c.Kind == "governance connector").Select(c => (c.Name, c.Outcome)).ShouldBe(
        [
            ("self-sign-up", Outcome.Unchanged),
            ("lite-user-sign-up", Outcome.Unchanged),
            ("account-recovery", Outcome.Unchanged),
        ]);
    }

    [Fact]
    public async Task Only_properties_not_already_false_are_patched()
    {
        Connector(Onboarding, "self-sign-up", ("SelfRegistration.Enable", "false"));
        Connector(Onboarding, "lite-user-sign-up", ("LiteRegistration.Enable", "false"));
        Connector(
            AccountManagement,
            "account-recovery",
            ("Recovery.Notification.Password.Enable", "true"),
            ("Recovery.Question.Password.Enable", "false"),
            ("Recovery.ReCaptcha.Password.Enable", "true"));
        _wso2.On(HttpMethod.Patch, ConnectorPath(AccountManagement, "account-recovery"), HttpStatusCode.OK);
        MyAccountIs(enabled: false);

        var state = await RunAsync();

        var patch = _wso2.Writes().ShouldHaveSingleItem().Body!;
        patch["operation"]!.GetValue<string>().ShouldBe("UPDATE");
        patch["properties"]!.AsArray().Select(p => (p!["name"]!.GetValue<string>(), p["value"]!.GetValue<string>())).ShouldBe(
        [
            ("Recovery.Notification.Password.Enable", "false"),
            ("Recovery.Notification.Username.Enable", "false"),
        ]);
        state.Changes.Single(c => c.Name == "account-recovery").Outcome.ShouldBe(Outcome.Updated);
    }

    [Fact]
    public async Task False_in_any_case_counts_as_switched_off()
    {
        Connector(Onboarding, "self-sign-up", ("SelfRegistration.Enable", "FALSE"));
        Connector(Onboarding, "lite-user-sign-up", ("LiteRegistration.Enable", "False"));
        Connector(
            AccountManagement,
            "account-recovery",
            ("Recovery.Notification.Password.Enable", "false"),
            ("Recovery.Question.Password.Enable", "false"),
            ("Recovery.Notification.Username.Enable", "false"));
        MyAccountIs(enabled: false);

        await RunAsync();

        _wso2.Writes().ShouldBeEmpty();
    }

    [Fact]
    public async Task Enabled_my_account_is_disabled_by_patching_only_that_flag()
    {
        AllConnectorsOff();
        MyAccountIs(enabled: true);
        _wso2.On(HttpMethod.Patch, MyAccount, HttpStatusCode.OK);

        var state = await RunAsync();

        var patch = _wso2.Writes().ShouldHaveSingleItem();
        patch.PathAndQuery.ShouldBe(MyAccount);
        patch.Body!.ToJsonString().ShouldBe("""{"applicationEnabled":false}""");
        state.Changes.Single(c => c.Kind == "application").Outcome.ShouldBe(Outcome.Updated);
    }

    [Fact]
    public async Task My_account_is_found_by_its_client_id()
    {
        AllConnectorsOff();
        MyAccountIs(enabled: false);

        await RunAsync();

        _wso2.Requests.ShouldContain(r => r.PathAndQuery == $"api/server/v1/applications?filter=clientId+eq+{SelfServiceStep.MyAccountClientId}");
    }

    [Fact]
    public async Task Disabled_my_account_is_left_alone()
    {
        AllConnectorsOff();
        MyAccountIs(enabled: false);

        var state = await RunAsync();

        _wso2.Writes().ShouldBeEmpty();
        state.Changes.Single(c => c.Kind == "application").Outcome.ShouldBe(Outcome.Unchanged);
    }

    [Fact]
    public async Task Missing_my_account_stops_the_step()
    {
        AllConnectorsOff();
        _wso2.OnJson(HttpMethod.Get, "api/server/v1/applications", """{"applications":[]}""");

        var failure = await Should.ThrowAsync<InvalidOperationException>(() => RunAsync());

        failure.Message.ShouldContain("My Account app was not found");
    }

    public void Dispose() => _wso2.Dispose();

    private static string ConnectorPath(string category, string connector) =>
        $"api/server/v1/identity-governance/{Wso2Ids.ForUri(category)}/connectors/{Wso2Ids.ForUri(connector)}";

    private void Connector(string category, string connector, params (string Name, string Value)[] properties) =>
        _wso2.OnJson(
            HttpMethod.Get,
            ConnectorPath(category, connector),
            new JsonObject
            {
                ["id"] = Wso2Ids.ForUri(connector),
                ["properties"] = new JsonArray([.. properties.Select(p => new JsonObject { ["name"] = p.Name, ["value"] = p.Value })]),
            }.ToJsonString());

    private void AllConnectorsOff()
    {
        Connector(Onboarding, "self-sign-up", ("SelfRegistration.Enable", "false"));
        Connector(Onboarding, "lite-user-sign-up", ("LiteRegistration.Enable", "false"));
        Connector(
            AccountManagement,
            "account-recovery",
            ("Recovery.Notification.Password.Enable", "false"),
            ("Recovery.Question.Password.Enable", "false"),
            ("Recovery.Notification.Username.Enable", "false"));
    }

    private void MyAccountIs(bool enabled) =>
        _wso2.OnJson(HttpMethod.Get, "api/server/v1/applications", """{"applications":[{"id":"my-account-1","name":"My Account"}]}""")
            .OnJson(HttpMethod.Get, MyAccount, $$"""{"id":"my-account-1","applicationEnabled":{{(enabled ? "true" : "false")}}}""");

    private async Task<BootstrapState> RunAsync(bool lockDown = true)
    {
        var state = new BootstrapState();
        var client = _wso2.Client();
        var options = Options.Create(new BootstrapOptions { LockDownSelfService = lockDown });
        await new SelfServiceStep(client, new Wso2Applications(client), options).RunAsync(state, TestContext.Current.CancellationToken);
        return state;
    }
}
