namespace RegReturns.ServiceDefaults;

/// <summary>Settings for logs, traces and metrics (configuration section <c>Observability</c>).</summary>
public sealed class ObservabilityOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Observability";

    /// <summary>Gets or sets the console log format: <c>text</c> (readable) or <c>json</c> (structured, for log shippers).</summary>
    public string ConsoleFormat { get; set; } = "json";

    /// <summary>
    /// Gets or sets the OTLP endpoint for logs, traces and metrics (for example <c>http://seq:5341/ingest/otlp</c>).
    /// Falls back to the standard <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> variable. Export is off when neither is set.
    /// </summary>
    public Uri? OtlpEndpoint { get; set; }
}
