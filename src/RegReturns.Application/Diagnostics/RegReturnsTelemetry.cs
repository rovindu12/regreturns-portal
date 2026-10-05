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

    /// <summary>Span and metric tag holding a submission id.</summary>
    public const string SubmissionIdTag = "regreturns.submission_id";

    /// <summary>Span and metric tag holding a workflow action.</summary>
    public const string WorkflowActionTag = "action";

    /// <summary>Span and metric tag holding the outcome of a use case.</summary>
    public const string OutcomeTag = "outcome";

    /// <summary>Span and metric tag holding the stable code of an expected failure.</summary>
    public const string ErrorCodeTag = "error_code";

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

    /// <summary>Gets the count of workflow steps, tagged with <c>action</c>, <c>outcome</c> (done, refused) and, when refused, <c>error_code</c>.</summary>
    public static Counter<long> WorkflowTransitions { get; } = Meter.CreateCounter<long>(
        "regreturns.workflow.transitions", "{step}", "Workflow steps by action and outcome.");

    /// <summary>Gets the count of returns first submitted after their due date.</summary>
    public static Counter<long> LateSubmissions { get; } = Meter.CreateCounter<long>(
        "regreturns.workflow.late_submissions", "{return}", "Returns first submitted after their due date.");
}
