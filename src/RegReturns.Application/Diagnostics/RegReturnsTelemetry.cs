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

    /// <summary>Gets the count of returns delivered through the API, tagged with <c>outcome</c> (created, updated, refused) and, when refused, <c>error_code</c>.</summary>
    public static Counter<long> Deliveries { get; } = Meter.CreateCounter<long>(
        "regreturns.api.deliveries", "{return}", "Returns delivered through the API by outcome.");

    /// <summary>Gets the count of workflow steps, tagged with <c>action</c>, <c>outcome</c> (done, refused) and, when refused, <c>error_code</c>.</summary>
    public static Counter<long> WorkflowTransitions { get; } = Meter.CreateCounter<long>(
        "regreturns.workflow.transitions", "{step}", "Workflow steps by action and outcome.");

    /// <summary>Gets the count of report exports, tagged with <c>report</c> and <c>format</c>.</summary>
    public static Counter<long> ReportExports { get; } = Meter.CreateCounter<long>(
        "regreturns.reports.exports", "{file}", "Report exports by report and format.");

    /// <summary>
    /// Gets the count of insight requests, tagged with <c>provider</c> (who wrote the narrative), <c>outcome</c>
    /// (generated, fallback, reused) and, for a fallback, <c>fallback_reason</c>.
    /// </summary>
    public static Counter<long> InsightsGenerated { get; } = Meter.CreateCounter<long>(
        "regreturns.insights.generated", "{insight}", "Advisory insights by provider and outcome.");

    /// <summary>Gets the time taken to generate an insight, provider call included, tagged with <c>provider</c> and <c>outcome</c>.</summary>
    public static Histogram<double> InsightDuration { get; } = Meter.CreateHistogram<double>(
        "regreturns.insights.duration", "ms", "Time to generate an advisory insight.");

    /// <summary>Gets the tokens billed by the AI provider, tagged with <c>model</c> and <c>direction</c> (input, output).</summary>
    public static Counter<long> AiTokens { get; } = Meter.CreateCounter<long>(
        "regreturns.ai.tokens", "{token}", "Tokens billed by the AI provider.");

    /// <summary>
    /// Gets the count of demo resets, tagged with <c>trigger</c> (Scheduled, Manual), <c>outcome</c> (done, refused)
    /// and, when refused, <c>error_code</c>.
    /// </summary>
    public static Counter<long> DemoResets { get; } = Meter.CreateCounter<long>(
        "regreturns.demo.resets", "{reset}", "Demo resets by trigger and outcome.");

    /// <summary>Gets the time a demo reset took, tagged with <c>trigger</c>.</summary>
    public static Histogram<double> DemoResetDuration { get; } = Meter.CreateHistogram<double>(
        "regreturns.demo.reset.duration", "ms", "Time to reset the demo data.");

    /// <summary>Gets the count of returns first submitted after their due date.</summary>
    public static Counter<long> LateSubmissions { get; } = Meter.CreateCounter<long>(
        "regreturns.workflow.late_submissions", "{return}", "Returns first submitted after their due date.");
}
