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

    /// <summary>Gets the meter for business metrics.</summary>
    public static Meter Meter { get; } = new(MeterName);

    /// <summary>Gets the count of validation runs, tagged with <c>outcome</c> (clean, warnings, errors).</summary>
    public static Counter<long> ValidationRuns { get; } = Meter.CreateCounter<long>(
        "regreturns.validation.runs", "{run}", "Validation runs by outcome.");

    /// <summary>Gets the count of validation findings, tagged with <c>rule_code</c> and <c>severity</c>.</summary>
    public static Counter<long> ValidationFindings { get; } = Meter.CreateCounter<long>(
        "regreturns.validation.findings", "{finding}", "Validation findings by rule and severity.");

    /// <summary>Gets the count of return file uploads, tagged with <c>outcome</c> and, when rejected, <c>reason</c>.</summary>
    public static Counter<long> Uploads { get; } = Meter.CreateCounter<long>(
        "regreturns.uploads", "{file}", "Return file uploads by outcome.");
}
