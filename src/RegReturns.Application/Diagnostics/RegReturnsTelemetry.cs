using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace RegReturns.Application.Diagnostics;

/// <summary>
/// Names and shared instances for custom tracing and metrics.
/// Everything starting with <see cref="Prefix"/> is collected by the OpenTelemetry setup in ServiceDefaults.
/// </summary>
public static class RegReturnsTelemetry
{
    /// <summary>Prefix shared by every RegReturns activity source and meter.</summary>
    public const string Prefix = "RegReturns";

    /// <summary>Name of the application activity source.</summary>
    public const string ActivitySourceName = Prefix + ".Application";

    /// <summary>Name of the application meter.</summary>
    public const string MeterName = Prefix + ".Application";

    /// <summary>Gets the activity source for custom spans around use cases.</summary>
    public static ActivitySource ActivitySource { get; } = new(ActivitySourceName);
}
