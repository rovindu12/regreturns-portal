using System.CommandLine;
using System.Globalization;

using RegReturns.Migrator.Commands;

using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    var root = new RootCommand("RegReturns database tool: applies schema migrations, loads demo data, migrates legacy returns and verifies the audit chain.")
    {
        DatabaseCommands.MigrateDb(),
        DatabaseCommands.Seed(),
        AuditCommands.VerifyAudit(),
        LegacyCommands.Legacy(),
        LegacyCommands.Samples(),
    };
    return await root.Parse(args).InvokeAsync();
}
finally
{
    await Log.CloseAndFlushAsync();
}
