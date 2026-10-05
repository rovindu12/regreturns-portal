extern alias IamBootstrapTool;

using IamBootstrapTool::RegReturns.IamBootstrap;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;

namespace RegReturns.UnitTests.IamBootstrap;

public sealed class BootstrapCommandsTests
{
    [Fact]
    public void Dot_env_values_go_just_before_the_unprefixed_environment_variables()
    {
        var hostVariables = new EnvironmentVariablesConfigurationSource { Prefix = "DOTNET_" };
        var appSettings = new JsonConfigurationSource { Path = "appsettings.json" };
        var variables = new EnvironmentVariablesConfigurationSource();
        var commandLine = new MemoryConfigurationSource();
        var dotEnv = new MemoryConfigurationSource();
        IList<IConfigurationSource> sources = [hostVariables, appSettings, variables, commandLine];

        BootstrapCommands.InsertDotEnv(sources, dotEnv);

        sources.ShouldBe([hostVariables, appSettings, dotEnv, variables, commandLine]);
    }

    [Fact]
    public void Dot_env_values_are_appended_when_there_is_no_environment_source()
    {
        var dotEnv = new MemoryConfigurationSource();
        IList<IConfigurationSource> sources = [new JsonConfigurationSource { Path = "appsettings.json" }];

        BootstrapCommands.InsertDotEnv(sources, dotEnv);

        sources[^1].ShouldBe(dotEnv);
    }
}
