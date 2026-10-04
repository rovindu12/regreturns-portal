using System.CommandLine;
using System.Globalization;

using RegReturns.Migrator.Commands;

using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    var root = new RootCommand("RegReturns database tool: applies schema migrations and loads demo data.")
    {
        DatabaseCommands.MigrateDb(),
        DatabaseCommands.Seed(),
    };
    return await root.Parse(args).InvokeAsync();
}
finally
{
    await Log.CloseAndFlushAsync();
}
