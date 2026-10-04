extern alias IamBootstrapTool;

using IamBootstrapTool::RegReturns.IamBootstrap;
using IamBootstrapTool::RegReturns.IamBootstrap.Steps;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace RegReturns.UnitTests.IamBootstrap;

public sealed class BootstrapRunnerTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("regreturns-runner-").FullName;
    private readonly List<string> _ran = [];

    [Fact]
    public async Task All_steps_run_in_registration_order()
    {
        await RunAsync([Step("claims"), Step("api-resource"), Step("portal-app")], only: []);

        _ran.ShouldBe(["claims", "api-resource", "portal-app"]);
    }

    [Fact]
    public async Task Only_the_named_steps_run()
    {
        await RunAsync([Step("claims"), Step("roles"), Step("demo-users")], only: ["demo-users"]);

        _ran.ShouldBe(["demo-users"]);
    }

    [Fact]
    public async Task Steps_share_one_state()
    {
        var steps = new[]
        {
            Step("first", s => s.ApiResourceId = "res-1"),
            Step("second", s => s.Record("check", s.ApiResourceId ?? "missing", Outcome.Unchanged)),
        };

        var state = await RunAsync(steps, only: []);

        state.Changes.ShouldHaveSingleItem().Name.ShouldBe("res-1");
    }

    [Fact]
    public async Task Reset_flag_reaches_the_steps()
    {
        var state = await RunAsync([Step("demo-users")], only: [], resetDemoUsers: true);

        state.ResetDemoUsers.ShouldBeTrue();
    }

    [Fact]
    public async Task Generated_settings_are_written_to_the_override_file()
    {
        var target = Path.Combine(_directory, "override.env");

        await RunAsync([Step("portal-app", s => s.GeneratedSettings["Oidc__ClientId"] = "regreturns-portal")], only: [], envFile: target);

        DotEnvFile.Read(target).ShouldBe(new Dictionary<string, string> { ["Oidc__ClientId"] = "regreturns-portal" });
        File.Exists(ConfiguredEnvFile).ShouldBeFalse();
    }

    [Fact]
    public async Task Generated_settings_go_to_the_configured_file_without_an_override()
    {
        await RunAsync([Step("portal-app", s => s.GeneratedSettings["Oidc__ClientId"] = "regreturns-portal")], only: []);

        DotEnvFile.Read(ConfiguredEnvFile)["Oidc__ClientId"].ShouldBe("regreturns-portal");
    }

    [Fact]
    public async Task No_file_is_written_when_nothing_was_generated()
    {
        await RunAsync([Step("claims")], only: []);

        Directory.GetFiles(_directory).ShouldBeEmpty();
    }

    [Fact]
    public async Task Failing_step_stops_the_run_before_later_steps_and_the_file()
    {
        var steps = new[]
        {
            Step("portal-app", s => s.GeneratedSettings["Oidc__ClientId"] = "regreturns-portal"),
            Step("roles", _ => throw new InvalidOperationException("WSO2 refused")),
            Step("demo-users"),
        };

        await Should.ThrowAsync<InvalidOperationException>(() => RunAsync(steps, only: []));

        _ran.ShouldBe(["portal-app", "roles"]);
        File.Exists(ConfiguredEnvFile).ShouldBeFalse();
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string ConfiguredEnvFile => Path.Combine(_directory, "configured.env");

    private Task<BootstrapState> RunAsync(IBootstrapStep[] steps, string[] only, bool resetDemoUsers = false, string? envFile = null)
    {
        var options = Options.Create(new BootstrapOptions { EnvFilePath = ConfiguredEnvFile });
        var runner = new BootstrapRunner(steps, options, NullLogger<BootstrapRunner>.Instance);
        return runner.RunAsync(only, envFile, resetDemoUsers, TestContext.Current.CancellationToken);
    }

    private FakeStep Step(string name, Action<BootstrapState>? action = null) => new(name, _ran, action);

    private sealed class FakeStep(string name, List<string> ran, Action<BootstrapState>? action) : IBootstrapStep
    {
        public string Name => name;

        public Task RunAsync(BootstrapState state, CancellationToken cancellationToken)
        {
            ran.Add(name);
            action?.Invoke(state);
            return Task.CompletedTask;
        }
    }
}
