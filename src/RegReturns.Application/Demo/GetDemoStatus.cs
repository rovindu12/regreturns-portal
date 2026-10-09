using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Auditing;

namespace RegReturns.Application.Demo;

/// <summary>When the demo resets itself (ADR 0031). Infrastructure implements it from <c>Demo:ResetSchedule</c>.</summary>
public interface IDemoResetSchedule
{
    /// <summary>Gets the cron expression in force, or <see langword="null"/> when the scheduled reset is off.</summary>
    string? Expression { get; }

    /// <summary>Returns the first scheduled reset after an instant.</summary>
    /// <param name="instant">The instant to look after.</param>
    /// <returns>The next reset (UTC), or <see langword="null"/> when there is none.</returns>
    DateTimeOffset? NextAfter(DateTimeOffset instant);
}

/// <summary>The schedule of a host that does not reset itself.</summary>
public sealed class NoDemoResetSchedule : IDemoResetSchedule
{
    /// <inheritdoc />
    public string? Expression => null;

    /// <inheritdoc />
    public DateTimeOffset? NextAfter(DateTimeOffset instant) => null;
}

/// <summary>Asks for the demo's reset history and schedule.</summary>
/// <param name="Recent">How many of the latest resets to list.</param>
public sealed record GetDemoStatus(int Recent = 5);

/// <summary>A reset, as its audit entry records it.</summary>
/// <param name="Sequence">The audit entry's sequence number.</param>
/// <param name="OccurredAt">When it committed.</param>
/// <param name="Actor">Who started it: a display name, or the system for a scheduled reset.</param>
/// <param name="Details">The entry's details.</param>
public sealed record DemoResetEntry(long Sequence, DateTimeOffset OccurredAt, string Actor, string? Details);

/// <summary>The demo's reset history and schedule.</summary>
/// <param name="Enabled">Whether this deployment is the demo.</param>
/// <param name="Recent">The latest resets, newest first.</param>
/// <param name="NextScheduledReset">When the job resets the demo next, if it is scheduled.</param>
/// <param name="ManualResetAvailableAt">When the administrator's button is accepted again, if it is cooling down now.</param>
/// <param name="CooldownMinutes">The button's cooldown.</param>
public sealed record DemoStatus(
    bool Enabled,
    IReadOnlyList<DemoResetEntry> Recent,
    DateTimeOffset? NextScheduledReset,
    DateTimeOffset? ManualResetAvailableAt,
    int CooldownMinutes)
{
    /// <summary>Gets the latest reset, if any.</summary>
    public DemoResetEntry? LastReset => Recent.Count > 0 ? Recent[0] : null;
}

/// <summary>Reads the latest <c>DemoReset</c> audit entries and the schedule. Reads nothing outside demo mode.</summary>
/// <param name="db">The unit of work.</param>
/// <param name="schedule">The reset schedule.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="options">The demo settings.</param>
public sealed class GetDemoStatusHandler(IAppDbContext db, IDemoResetSchedule schedule, TimeProvider timeProvider, IOptions<DemoOptions> options)
    : IQueryHandler<GetDemoStatus, DemoStatus>
{
    /// <inheritdoc />
    public async Task<DemoStatus> HandleAsync(GetDemoStatus query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return new DemoStatus(false, [], null, null, settings.ResetCooldownMinutes);
        }

        var now = timeProvider.GetUtcNow();
        var recent = await db.AuditEntries
            .Where(e => e.Action == AuditAction.DemoReset)
            .OrderByDescending(e => e.Sequence)
            .Take(Math.Clamp(query.Recent, 1, 50))
            .Select(e => new DemoResetEntry(e.Sequence, e.OccurredAt, e.ActorDisplayName ?? e.ActorSubjectId, e.Details))
            .ToListAsync(cancellationToken);

        var availableAt = DemoErrors.AvailableAt(recent.FirstOrDefault()?.OccurredAt, TimeSpan.FromMinutes(settings.ResetCooldownMinutes));
        return new DemoStatus(
            true,
            recent,
            schedule.NextAfter(now),
            availableAt > now ? availableAt : null,
            settings.ResetCooldownMinutes);
    }
}
