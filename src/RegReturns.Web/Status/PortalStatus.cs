using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using RegReturns.ServiceDefaults.Web;

namespace RegReturns.Web.Status;

/// <summary>How a part of the service is doing, as the public status page says it.</summary>
public enum StatusLevel
{
    /// <summary>Working.</summary>
    Operational = 0,

    /// <summary>Working, with problems.</summary>
    Degraded = 1,

    /// <summary>Not working.</summary>
    Down = 2,
}

/// <summary>One part of the service on the status page.</summary>
/// <param name="Name">What people call it.</param>
/// <param name="Purpose">What depends on it.</param>
/// <param name="Level">How it is doing.</param>
public sealed record ComponentStatus(string Name, string Purpose, StatusLevel Level);

/// <summary>The status page's view of the service.</summary>
/// <param name="Overall">The worst level of any component.</param>
/// <param name="Components">The portal and each readiness check.</param>
/// <param name="CheckedAt">When the checks ran.</param>
public sealed record PortalStatusReport(StatusLevel Overall, IReadOnlyList<ComponentStatus> Components, DateTimeOffset CheckedAt);

/// <summary>
/// Runs the readiness checks (<c>ready</c> tag: database, WSO2) for the public status page (ADR 0031), at most once
/// per <see cref="CacheFor"/> so anonymous visitors cannot load the database or WSO2 through it. Shows levels only,
/// never a check's description or exception.
/// </summary>
/// <param name="health">The health check service.</param>
/// <param name="cache">Holds the latest report.</param>
/// <param name="timeProvider">The clock.</param>
public sealed class PortalStatus(HealthCheckService health, IMemoryCache cache, TimeProvider timeProvider)
{
    /// <summary>How long a report is reused.</summary>
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(15);

    private const string CacheKey = "RegReturns.PortalStatus";

    private static readonly Dictionary<string, (string Name, string Purpose)> Labels = new(StringComparer.Ordinal)
    {
        ["database"] = ("Database", "Returns, templates, reports and the audit trail"),
        ["wso2"] = ("Identity server", "Signing in to the portal and API tokens"),
    };

    /// <summary>Returns the latest report, running the checks when the cached one is older than <see cref="CacheFor"/>.</summary>
    /// <param name="cancellationToken">Cancels the checks.</param>
    /// <returns>The report.</returns>
    public async Task<PortalStatusReport> GetAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out PortalStatusReport? cached) && cached is not null)
        {
            return cached;
        }

        var result = await health.CheckHealthAsync(check => check.Tags.Contains(WebDefaultsExtensions.ReadyTag), cancellationToken);
        var report = ToReport(result, timeProvider.GetUtcNow());
        cache.Set(CacheKey, report, CacheFor);
        return report;
    }

    /// <summary>Turns a health report into the page's view: the portal itself first, then each check in a fixed order.</summary>
    /// <param name="result">The health report.</param>
    /// <param name="checkedAt">When it ran.</param>
    /// <returns>The status report.</returns>
    public static PortalStatusReport ToReport(HealthReport result, DateTimeOffset checkedAt)
    {
        ArgumentNullException.ThrowIfNull(result);
        var components = new List<ComponentStatus> { new("Portal", "These pages", StatusLevel.Operational) };
        components.AddRange(result.Entries
            .OrderBy(e => Labels.ContainsKey(e.Key) ? 0 : 1)
            .ThenBy(e => e.Key, StringComparer.Ordinal)
            .Select(e =>
            {
                var (name, purpose) = Labels.GetValueOrDefault(e.Key, (e.Key, "A service the portal depends on"));
                return new ComponentStatus(name, purpose, LevelOf(e.Value.Status));
            }));
        return new PortalStatusReport(components.Max(c => c.Level), components, checkedAt);
    }

    private static StatusLevel LevelOf(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => StatusLevel.Operational,
        HealthStatus.Degraded => StatusLevel.Degraded,
        _ => StatusLevel.Down,
    };
}
