using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RegReturns.Application.Idempotency;

namespace RegReturns.Infrastructure.Idempotency;

/// <summary>
/// Deletes expired idempotency records on a fixed interval (<see cref="IdempotencyOptions.PurgeIntervalMinutes"/>).
/// A failed purge is logged and tried again at the next interval; it never stops the host.
/// </summary>
/// <param name="scopes">Creates a scope per purge.</param>
/// <param name="options">The purge interval.</param>
/// <param name="timeProvider">The clock that drives the timer.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class IdempotencyPurger(
    IServiceScopeFactory scopes,
    IOptions<IdempotencyOptions> options,
    TimeProvider timeProvider,
    ILogger<IdempotencyPurger> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.Value.PurgeIntervalMinutes), timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await PurgeOnceAsync(stoppingToken);
        }
    }

    /// <summary>Runs one purge, logging rather than throwing a failure.</summary>
    /// <param name="cancellationToken">Cancels the purge.</param>
    /// <returns>A task that completes when the purge has run.</returns>
    internal async Task PurgeOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IIdempotencyStore>().PurgeExpiredAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogPurgeFailed(logger, exception);
        }
    }

    [LoggerMessage(EventId = 3307, Level = LogLevel.Warning, Message = "Could not purge expired idempotency records; trying again later")]
    private static partial void LogPurgeFailed(ILogger logger, Exception exception);
}
