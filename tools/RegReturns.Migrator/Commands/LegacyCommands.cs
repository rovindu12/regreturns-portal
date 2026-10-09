using System.CommandLine;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using RegReturns.Application.Auditing;
using RegReturns.Application.Migration;
using RegReturns.Domain.Migration;
using RegReturns.Infrastructure;
using RegReturns.Infrastructure.Legacy;
using RegReturns.Migrator.Legacy;
using RegReturns.ServiceDefaults;

namespace RegReturns.Migrator.Commands;

/// <summary>Commands that migrate a legacy returns system's exports (ADR 0029).</summary>
internal static partial class LegacyCommands
{
    /// <summary>Exit code: the run reconciled (and committed, unless it was a dry run).</summary>
    public const int Reconciled = 0;

    /// <summary>Exit code: the run could not start or failed.</summary>
    public const int Failed = 1;

    /// <summary>Exit code: source and target do not reconcile, so nothing was committed.</summary>
    public const int Mismatch = 2;

    /// <summary>The mapping file looked for in the source folder when <c>--mapping</c> is not given.</summary>
    public const string DefaultMappingFile = "mapping.json";

    /// <summary><c>legacy --source &lt;dir&gt; [--mapping &lt;file&gt;] [--dry-run] [--report &lt;dir&gt;]</c>.</summary>
    /// <returns>The command.</returns>
    public static Command Legacy()
    {
        var source = new Option<DirectoryInfo>("--source")
        {
            Description = "Folder holding the legacy CSV exports.",
            Required = true,
        };
        var mapping = new Option<FileInfo?>("--mapping")
        {
            Description = $"JSON mapping of banks, columns and cleansing rules (default: {DefaultMappingFile} in the source folder).",
        };
        var dryRun = new Option<bool>("--dry-run")
        {
            Description = "Load, validate and reconcile inside a transaction, then roll everything back.",
        };
        var report = new Option<DirectoryInfo?>("--report")
        {
            Description = "Folder to write the CSV reports to (summary, row errors, reconciliation).",
        };
        var command = new Command("legacy", "Migrate a legacy returns system's CSV exports as approved historical returns.")
        {
            source, mapping, dryRun, report,
        };
        command.SetAction((parse, ct) =>
        {
            var sourceFolder = parse.GetValue(source)!;
            var request = new LegacyMigrationRequest(
                sourceFolder.ToString(),
                parse.GetValue(mapping)?.ToString() ?? Path.Combine(sourceFolder.ToString(), DefaultMappingFile),
                parse.GetValue(dryRun));
            return MigrateAsync(request, parse.GetValue(report)?.ToString(), Console.Out, ct);
        });
        return command;
    }

    /// <summary><c>legacy-samples --out &lt;dir&gt;</c>: writes the sample exports and their mapping.</summary>
    /// <returns>The command.</returns>
    public static Command Samples()
    {
        var output = new Option<DirectoryInfo>("--out")
        {
            Description = "Folder to write the sample exports and mapping to, such as samples/legacy.",
            Required = true,
        };
        var command = new Command("legacy-samples", "Write the sample legacy exports and their mapping.") { output };
        command.SetAction((parse, ct) => WriteSamplesAsync(parse.GetValue(output)!.ToString(), Console.Out, ct));
        return command;
    }

    /// <summary>Runs a migration in its own host and prints the report.</summary>
    /// <param name="request">What to migrate.</param>
    /// <param name="reportFolder">Where to write the CSV reports, if anywhere.</param>
    /// <param name="output">Where to print the console report.</param>
    /// <param name="cancellationToken">A token to cancel the run.</param>
    /// <returns>The exit code.</returns>
    internal static async Task<int> MigrateAsync(
        LegacyMigrationRequest request, string? reportFolder, TextWriter output, CancellationToken cancellationToken)
    {
        // Content root is the tool's own folder so its appsettings.json loads whatever the working directory is.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
        builder.AddObservability("regreturns-migrator");
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddAuditTrail(builder.Configuration);
        builder.Services.AddSingleton<IAuditContext, MigratorAuditContext>();
        builder.Services.AddLegacyMigration();

        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RegReturns.Migrator");
        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            return await MigrateAsync(scope.ServiceProvider, request, reportFolder, output, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex);
            await output.WriteLineAsync($"The migration failed: {ex.GetType().Name}. See the log for details; nothing was committed.");
            return Failed;
        }
    }

    /// <summary>Runs a migration with the given services and prints the report.</summary>
    /// <param name="services">Services providing <see cref="ILegacyMigrator"/> and <see cref="IMigrationReportWriter"/>.</param>
    /// <param name="request">What to migrate.</param>
    /// <param name="reportFolder">Where to write the CSV reports, if anywhere.</param>
    /// <param name="output">Where to print the console report.</param>
    /// <param name="cancellationToken">A token to cancel the run.</param>
    /// <returns>The exit code.</returns>
    internal static async Task<int> MigrateAsync(
        IServiceProvider services, LegacyMigrationRequest request, string? reportFolder, TextWriter output, CancellationToken cancellationToken)
    {
        var result = await services.GetRequiredService<ILegacyMigrator>().RunAsync(request, cancellationToken);
        if (result.IsFailure)
        {
            await output.WriteLineAsync($"The migration could not start: {result.Error!.Message} ({result.Error.Code})");
            return Failed;
        }

        LegacyConsoleReport.Write(result.Value, output);
        if (reportFolder is not null)
        {
            var written = await services.GetRequiredService<IMigrationReportWriter>().WriteAsync(result.Value, reportFolder, cancellationToken);
            await output.WriteLineAsync($"Reports written to {reportFolder}: {string.Join(", ", written.Select(Path.GetFileName))}.");
        }

        return result.Value.Outcome == MigrationOutcome.Reconciled ? Reconciled : Mismatch;
    }

    /// <summary>Writes the sample exports and mapping.</summary>
    /// <param name="folder">The folder to write to.</param>
    /// <param name="output">Where to print what was written.</param>
    /// <param name="cancellationToken">A token to cancel the writes.</param>
    /// <returns>The exit code.</returns>
    internal static async Task<int> WriteSamplesAsync(string folder, TextWriter output, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folder);
        foreach (var (name, content) in LegacySamples.Generate())
        {
            await File.WriteAllBytesAsync(Path.Combine(folder, name), content, cancellationToken);
            await output.WriteLineAsync($"Wrote {Path.Combine(folder, name)}");
        }

        return Reconciled;
    }

    [LoggerMessage(EventId = 2106, Level = LogLevel.Critical, Message = "Legacy migration failed; nothing was committed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
