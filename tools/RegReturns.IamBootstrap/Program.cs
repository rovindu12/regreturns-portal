using System.CommandLine;
using System.Globalization;

using RegReturns.IamBootstrap;

using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    var root = new RootCommand(
        "RegReturns IAM setup: creates or updates every WSO2 object RegReturns needs (claims, scopes, API resource, " +
        "apps, roles, demo users) and writes generated client secrets to a git-ignored env file. Safe to re-run.")
    {
        BootstrapCommands.Apply(),
        BootstrapCommands.DemoUsers(),
    };
    return await root.Parse(args).InvokeAsync();
}
finally
{
    await Log.CloseAndFlushAsync();
}
