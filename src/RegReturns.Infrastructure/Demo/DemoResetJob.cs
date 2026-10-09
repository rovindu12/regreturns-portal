using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using RegReturns.Application.Demo;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Common;

namespace RegReturns.Infrastructure.Demo;

/// <summary>
/// Resets the demo at every occurrence of <c>Demo:ResetSchedule</c> (ADR 0031), acting as the system. Does nothing
/// when demo mode or the schedule is off. A failed reset is logged and tried at the next occurrence; it never stops
/// the host.
/// </summary>
/// <param name="scopes">Creates a scope per reset.</param>
/// <param name="schedule">When to reset.</param>
/// <param name="timeProvider">The clock that drives the waits.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class DemoResetJob(
    IServiceScopeFactory scopes,
    IDemoResetSchedule schedule,
    TimeProvider timeProvider,
    ILogger<DemoResetJob> logger) : BackgroundService
{
    // Task.Delay cannot wait longer than about 49 days; long gaps are waited out in steps.
    private static readonly TimeSpan LongestWait = TimeSpan.FromDays(1);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (schedule.Expression is null)
        {
            LogOff(logger);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var next = schedule.NextAfter(timeProvider.GetUtcNow());
            if (next is null)
            {
                return;
            }

            LogScheduled(logger, schedule.Expression, next.Value);
            for (var wait = next.Value - timeProvider.GetUtcNow(); wait > TimeSpan.Zero; wait = next.Value - timeProvider.GetUtcNow())
            {
                await Task.Delay(wait < LongestWait ? wait : LongestWait, timeProvider, stoppingToken);
            }

            await RunOnceAsync(stoppingToken);
        }
    }

    /// <summary>Runs one scheduled reset, logging rather than throwing a failure.</summary>
    /// <param name="cancellationToken">Cancels the reset.</param>
    /// <returns>A task that completes when the reset has run or failed.</returns>
    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ResetDemo, Result<DemoResetReport>>>();
            await handler.HandleAsync(new ResetDemo(DemoResetTrigger.Scheduled), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(logger, exception);
        }
    }

    [LoggerMessage(EventId = 5603, Level = LogLevel.Information, Message = "Next scheduled demo reset ({Schedule}) at {NextReset:u}")]
    private static partial void LogScheduled(ILogger logger, string schedule, DateTimeOffset nextReset);

    [LoggerMessage(EventId = 5604, Level = LogLevel.Information, Message = "Scheduled demo reset is off (demo mode or Demo:ResetSchedule not set)")]
    private static partial void LogOff(ILogger logger);

    [LoggerMessage(EventId = 5605, Level = LogLevel.Error, Message = "Scheduled demo reset failed; nothing was changed, trying again at the next occurrence")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
