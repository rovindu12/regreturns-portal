extern alias IamBootstrapTool;

using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Nodes;

using IamBootstrapTool::RegReturns.IamBootstrap;
using IamBootstrapTool::RegReturns.IamBootstrap.Steps;
using IamBootstrapTool::RegReturns.IamBootstrap.Wso2;

using Microsoft.Extensions.Options;

namespace RegReturns.UnitTests.IamBootstrap;

public sealed class AccountLockStepTests : IDisposable
{
    private static readonly string Path =
        $"api/server/v1/identity-governance/{Wso2Ids.ForUri(AccountLockStep.Category)}/connectors/{Wso2Ids.ForUri(AccountLockStep.Connector)}";

    private readonly StubWso2 _wso2 = new();

    [Fact]
    public void The_connector_ids_are_the_ones_wso2_uses()
    {
        Path.ShouldBe("api/server/v1/identity-governance/TG9naW4gQXR0ZW1wdHMgU2VjdXJpdHk/connectors/YWNjb3VudC5sb2NrLmhhbmRsZXI");
    }

    [Fact]
    public async Task Wso2s_defaults_are_changed_to_lock_after_five_failures_for_five_minutes_without_growth()
    {
        // WSO2 7.3 out of the box.
        Connector(
            ("account.lock.handler.notification.manageInternally", "true"),
            ("account.lock.handler.login.fail.timeout.ratio", "2"),
            ("account.lock.handler.Time", "5"),
            ("account.lock.handler.lock.on.max.failed.attempts.enable", "false"),
            ("account.lock.handler.On.Failure.Max.Attempts", "5"));
        _wso2.On(HttpMethod.Patch, Path, HttpStatusCode.OK);

        var state = await RunAsync(new BootstrapOptions());

        var patch = _wso2.Writes().ShouldHaveSingleItem().Body!;
        patch["operation"]!.GetValue<string>().ShouldBe("UPDATE");
        Properties(patch).ShouldBe(
        [
            ("account.lock.handler.lock.on.max.failed.attempts.enable", "true"),
            ("account.lock.handler.login.fail.timeout.ratio", "1"),
            ("account.lock.handler.notification.manageInternally", "false"),
        ]);
        state.Changes.ShouldHaveSingleItem().Outcome.ShouldBe(Outcome.Updated);
    }

    [Fact]
    public async Task The_attempts_and_minutes_come_from_the_options()
    {
        Connector();
        _wso2.On(HttpMethod.Patch, Path, HttpStatusCode.OK);

        await RunAsync(new BootstrapOptions { FailedSignInsBeforeLock = 3, AccountLockMinutes = 30 });

        var properties = Properties(_wso2.Writes().ShouldHaveSingleItem().Body!);
        properties.ShouldContain(("account.lock.handler.On.Failure.Max.Attempts", "3"));
        properties.ShouldContain(("account.lock.handler.Time", "30"));
    }

    [Fact]
    public async Task A_connector_already_set_is_left_unchanged()
    {
        Connector(
            ("account.lock.handler.notification.manageInternally", "false"),
            ("account.lock.handler.login.fail.timeout.ratio", "1"),
            ("account.lock.handler.Time", "5"),
            ("account.lock.handler.lock.on.max.failed.attempts.enable", "TRUE"),
            ("account.lock.handler.On.Failure.Max.Attempts", "5"));

        var state = await RunAsync(new BootstrapOptions());

        _wso2.Writes().ShouldBeEmpty();
        state.Changes.ShouldHaveSingleItem().Outcome.ShouldBe(Outcome.Unchanged);
    }

    [Theory]
    [InlineData(2, 5)]
    [InlineData(21, 5)]
    [InlineData(5, 0)]
    [InlineData(5, 1441)]
    public void Out_of_range_settings_are_refused(int attempts, int minutes)
    {
        var options = new BootstrapOptions { FailedSignInsBeforeLock = attempts, AccountLockMinutes = minutes };

        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        results.ShouldContain(r => r.MemberNames.Contains(nameof(BootstrapOptions.FailedSignInsBeforeLock))
            || r.MemberNames.Contains(nameof(BootstrapOptions.AccountLockMinutes)));
    }

    public void Dispose() => _wso2.Dispose();

    private static List<(string, string)> Properties(JsonNode patch) =>
        [.. patch["properties"]!.AsArray().Select(p => (p!["name"]!.GetValue<string>(), p["value"]!.GetValue<string>()))];

    private void Connector(params (string Name, string Value)[] properties) =>
        _wso2.OnJson(
            HttpMethod.Get,
            Path,
            new JsonObject
            {
                ["id"] = Wso2Ids.ForUri(AccountLockStep.Connector),
                ["properties"] = new JsonArray([.. properties.Select(p => new JsonObject { ["name"] = p.Name, ["value"] = p.Value })]),
            }.ToJsonString());

    private async Task<BootstrapState> RunAsync(BootstrapOptions options)
    {
        var state = new BootstrapState();
        await new AccountLockStep(_wso2.Client(), Options.Create(options)).RunAsync(state, TestContext.Current.CancellationToken);
        return state;
    }
}
