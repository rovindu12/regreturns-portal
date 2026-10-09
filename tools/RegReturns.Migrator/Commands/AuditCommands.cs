using System.CommandLine;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using RegReturns.Application.Auditing;
using RegReturns.Infrastructure;
using RegReturns.Migrator.Auditing;
using RegReturns.ServiceDefaults;

namespace RegReturns.Migrator.Commands;

/// <summary>Commands that check the audit trail (ADR 0016) from the command line, as the restore runbook does (ADR 0034).</summary>
internal static partial class AuditCommands
{
    /// <summary>Exit code: every entry checked out.</summary>
    public const int Intact = 0;

    /// <summary>Exit code: the check could not run.</summary>
    public const int Failed = 1;

    /// <summary>Exit code: the chain is broken.</summary>
    public const int Broken = 2;

    /// <summary><c>verify-audit</c>: walks the hash chain and records the check in it, as the auditor's screen does.</summary>
    /// <returns>The command.</returns>
    public static Command VerifyAudit()
    {
        var command = new Command(
            "verify-audit",
            "Verify the audit hash chain and record the check in it (exit code 0 intact, 2 broken, 1 failed).");
        command.SetAction((_, ct) => VerifyAsync(Console.Out, ct));
        return command;
    }

    /// <summary>Verifies the chain in its own host and prints the outcome.</summary>
    /// <param name="output">Where to print the outcome.</param>
    /// <param name="cancellationToken">A token to cancel the check.</param>
    /// <returns>The exit code.</returns>
    internal static async Task<int> VerifyAsync(TextWriter output, CancellationToken cancellationToken)
    {
        // Content root is the tool's own folder so its appsettings.json loads whatever the working directory is.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
        builder.AddObservability("regreturns-migrator");
        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddAuditTrail(builder.Configuration);
        builder.Services.AddSingleton<IAuditContext>(MigratorAuditContext.ChainVerification);

        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RegReturns.Migrator");
        try
        {
            await using var scope = host.Services.CreateAsyncScope();
            return await VerifyAsync(scope.ServiceProvider, logger, output, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex);
            await output.WriteLineAsync($"The audit chain could not be verified: {ex.GetType().Name}. See the log for details.");
            return Failed;
        }
    }

    /// <summary>Verifies the chain with the given services and prints the outcome.</summary>
    /// <param name="services">Services providing the audit trail and its verifier.</param>
    /// <param name="logger">The command's logger.</param>
    /// <param name="output">Where to print the outcome.</param>
    /// <param name="cancellationToken">A token to cancel the check.</param>
    /// <returns>The exit code.</returns>
    internal static async Task<int> VerifyAsync(
        IServiceProvider services, ILogger logger, TextWriter output, CancellationToken cancellationToken)
    {
        var handler = ActivatorUtilities.CreateInstance<VerifyAuditChainHandler>(services);
        var result = await handler.HandleAsync(new VerifyAuditChain(), cancellationToken);
        await output.WriteLineAsync(result.Describe());
        if (result.IsIntact)
        {
            LogIntact(logger, result.EntriesChecked, result.HeadSequence);
            return Intact;
        }

        LogBroken(logger, result.FirstBreak!.Sequence, result.FirstBreak.Kind);
        return Broken;
    }

    [LoggerMessage(EventId = 2003, Level = LogLevel.Information, Message = "Audit chain intact: {EntriesChecked} entries checked up to {HeadSequence}")]
    private static partial void LogIntact(ILogger logger, long entriesChecked, long headSequence);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Critical, Message = "Audit chain broken at entry {Sequence} ({Kind})")]
    private static partial void LogBroken(ILogger logger, long sequence, ChainBreakKind kind);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Critical, Message = "Audit chain verification failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
