using System.Globalization;

using Microsoft.Extensions.Diagnostics.HealthChecks;

using RegReturns.Application.Diagnostics;
using RegReturns.Web.Status;

namespace RegReturns.Web.Models.Admin;

/// <summary>Data for the administrator's diagnostics page (ADR 0033).</summary>
/// <param name="Data">The database, the audit chain and the directory.</param>
/// <param name="Host">The build, runtime, health checks and settings.</param>
public sealed record DiagnosticsViewModel(DiagnosticsReport Data, HostDiagnostics Host)
{
    /// <summary>Returns the Bootstrap badge classes of a health status.</summary>
    /// <param name="status">The status.</param>
    /// <returns>The CSS classes.</returns>
    public static string Badge(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => "text-bg-success",
        HealthStatus.Degraded => "text-bg-warning",
        _ => "text-bg-danger",
    };

    /// <summary>Formats an uptime in days, hours and minutes.</summary>
    /// <param name="uptime">The uptime.</param>
    /// <returns>For example <c>2 d 03 h 15 min</c>.</returns>
    public static string Duration(TimeSpan uptime) =>
        uptime.TotalDays >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)uptime.TotalDays} d {uptime.Hours:00} h {uptime.Minutes:00} min")
            : string.Create(CultureInfo.InvariantCulture, $"{uptime.Hours} h {uptime.Minutes:00} min");
}
