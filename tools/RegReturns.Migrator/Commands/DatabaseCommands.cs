using System.CommandLine;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using RegReturns.Application.Abstractions;
using RegReturns.Infrastructure;
using RegReturns.ServiceDefaults;

namespace RegReturns.Migrator.Commands;

/// <summary>Commands that manage the RegReturns database schema and demo data.</summary>
internal static partial class DatabaseCommands
{
    private const int Success = 0;
    private const int Failure = 1;

    /// <summary><c>migrate-db [--seed]</c>: applies pending migrations, optionally loading demo data afterwards.</summary>
    public static Command MigrateDb()
    {
        var seedOption = new Option<bool>("--seed") { Description = "Load the demo data set after migrating, if the database is empty." };
        var command = new Command("migrate-db", "Apply pending EF Core schema migrations.") { seedOption };
        command.SetAction((parse, ct) => RunAsync(async (initializer, logger, token) =>
        {
            var applied = await initializer.MigrateAsync(token);
            LogMigrated(logger, applied.Count);
            if (parse.GetValue(seedOption))
            {
                await initializer.SeedAsync(token);
            }
        }, ct));
        return command;
    }

    /// <summary><c>seed</c>: loads the demo data set if the database is empty.</summary>
    public static Command Seed()
    {
        var command = new Command("seed", "Load the demo data set if the database is empty.");
        command.SetAction((_, ct) => RunAsync((initializer, _, token) => initializer.SeedAsync(token), ct));
        return command;
    }

    private static async Task<int> RunAsync(
        Func<IDatabaseInitializer, ILogger, CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        // Content root is the tool's own folder so its appsettings.json loads whatever the working directory is.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.AddObservability("regreturns-migrator");
        builder.Services.AddInfrastructure(builder.Configuration);

        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RegReturns.Migrator");
        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            await work(scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>(), logger, cancellationToken);
            return Success;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex);
            return Failure;
        }
    }

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Migration finished; {Count} migration(s) applied")]
    private static partial void LogMigrated(ILogger logger, int count);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Critical, Message = "Database command failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
