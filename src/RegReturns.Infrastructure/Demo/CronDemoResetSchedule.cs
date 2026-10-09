using Cronos;

using Microsoft.Extensions.Options;

using RegReturns.Application.Demo;

namespace RegReturns.Infrastructure.Demo;

/// <summary>
/// The demo's reset schedule from <c>Demo:ResetSchedule</c> (ADR 0031): a five-field cron expression in UTC, off when
/// it is empty or demo mode is off. The expression is validated at start-up.
/// </summary>
internal sealed class CronDemoResetSchedule : IDemoResetSchedule
{
    private readonly CronExpression? _cron;

    /// <summary>Initializes a new instance of the <see cref="CronDemoResetSchedule"/> class.</summary>
    /// <param name="options">The demo settings.</param>
    public CronDemoResetSchedule(IOptions<DemoOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var settings = options.Value;
        if (settings.Enabled && !string.IsNullOrWhiteSpace(settings.ResetSchedule))
        {
            Expression = settings.ResetSchedule.Trim();
            _cron = CronExpression.Parse(Expression);
        }
    }

    /// <inheritdoc />
    public string? Expression { get; }

    /// <inheritdoc />
    public DateTimeOffset? NextAfter(DateTimeOffset instant) => _cron?.GetNextOccurrence(instant, TimeZoneInfo.Utc);

    /// <summary>Returns whether a setting is empty or a valid five-field cron expression.</summary>
    /// <param name="expression">The setting.</param>
    /// <returns><see langword="true"/> when it can be used.</returns>
    public static bool IsValid(string? expression) =>
        string.IsNullOrWhiteSpace(expression) || CronExpression.TryParse(expression.Trim(), out _);
}
