using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

using RegReturns.ServiceDefaults.Logging;

using Serilog;
using Serilog.Formatting.Compact;
using Serilog.Sinks.OpenTelemetry;

namespace RegReturns.ServiceDefaults;

/// <summary>
/// Structured logging (Serilog) and distributed tracing and metrics (OpenTelemetry) shared by every RegReturns process.
/// Every log event carries the W3C trace id, so logs, traces and audit entries join on one value.
/// </summary>
public static class ObservabilityExtensions
{
    /// <summary>Activity sources and meters whose names start with this prefix are collected.</summary>
    public const string TelemetryPrefix = "RegReturns";

    private const string ReadableTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj} {TraceId}{NewLine}{Exception}";

    /// <summary>Adds Serilog and OpenTelemetry using the <c>Serilog</c> and <c>Observability</c> configuration sections.</summary>
    /// <param name="builder">The host builder.</param>
    /// <param name="serviceName">The service name reported in logs and traces, for example <c>regreturns-web</c>.</param>
    /// <returns>The same builder.</returns>
    public static IHostApplicationBuilder AddObservability(this IHostApplicationBuilder builder, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = builder.Configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>()
            ?? new ObservabilityOptions();
        var otlpEndpoint = options.OtlpEndpoint ?? ParseUri(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        builder.Services.AddSerilog((services, logger) =>
        {
            logger.ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Service", serviceName)
                .Enrich.WithProperty("Version", BuildInfo.Version)
                .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
                .Enrich.With<SensitiveDataEnricher>()
                .Destructure.With<SensitiveDataDestructuringPolicy>();

            if (string.Equals(options.ConsoleFormat, "text", StringComparison.OrdinalIgnoreCase))
            {
                logger.WriteTo.Console(outputTemplate: ReadableTemplate, formatProvider: System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                logger.WriteTo.Console(new RenderedCompactJsonFormatter());
            }

            if (otlpEndpoint is not null)
            {
                logger.WriteTo.OpenTelemetry(sink =>
                {
                    sink.Endpoint = new Uri(otlpEndpoint, "v1/logs").ToString();
                    sink.Protocol = OtlpProtocol.HttpProtobuf;
                    sink.ResourceAttributes = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["service.name"] = serviceName,
                        ["service.version"] = BuildInfo.Version,
                    };
                });
            }
        });

        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName, serviceVersion: BuildInfo.Version))
            .WithTracing(tracing => tracing
                .AddSource($"{TelemetryPrefix}.*")
                .AddAspNetCoreInstrumentation(o => o.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation()
                .AddSqlClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddMeter($"{TelemetryPrefix}.*")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation());

        if (otlpEndpoint is not null)
        {
            telemetry.UseOtlpExporter(OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf, otlpEndpoint);
        }

        return builder;
    }

    private static Uri? ParseUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
}
