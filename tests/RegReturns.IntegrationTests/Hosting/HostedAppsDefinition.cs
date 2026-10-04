namespace RegReturns.IntegrationTests.Hosting;

/// <summary>
/// Hosted-app tests run one at a time: each host replaces Serilog's static bootstrap logger and
/// WebApplicationFactory intercepts the first host built in the process, so parallel hosts interfere.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HostedAppsDefinition
{
    public const string Name = "Hosted apps";

    private HostedAppsDefinition()
    {
    }
}
