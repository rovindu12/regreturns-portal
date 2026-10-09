using System.Diagnostics;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

using Serilog;
using Serilog.Events;

namespace RegReturns.ServiceDefaults.Web;

/// <summary>Defaults shared by the web portal and the API: problem details, health endpoints, request logging.</summary>
public static class WebDefaultsExtensions
{
    /// <summary>Health check tag for liveness checks (the process is running).</summary>
    public const string LiveTag = "live";

    /// <summary>Health check tag for readiness checks (dependencies are reachable).</summary>
    public const string ReadyTag = "ready";

    /// <summary>Response header carrying the W3C trace id, so users and clients can quote it.</summary>
    public const string TraceIdHeader = "X-Trace-Id";

    /// <summary>Liveness endpoint path.</summary>
    public const string LivePath = "/health/live";

    /// <summary>Readiness endpoint path.</summary>
    public const string ReadyPath = "/health/ready";

    /// <summary>Adds observability, problem details with trace ids, the global exception handler and a liveness check.</summary>
    /// <param name="builder">The web application builder.</param>
    /// <param name="serviceName">The service name for logs and traces.</param>
    /// <returns>The same builder.</returns>
    public static WebApplicationBuilder AddServiceDefaults(this WebApplicationBuilder builder, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddObservability(serviceName);

        // Don't advertise the server software (ADR 0033).
        builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
            context.ProblemDetails.Extensions["traceId"] = CurrentTraceId(context.HttpContext);
        });
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), tags: [LiveTag]);
        return builder;
    }

    /// <summary>Adds the trace id response header and one summary log line per request.</summary>
    /// <param name="app">The application.</param>
    /// <returns>The same application.</returns>
    public static WebApplication UseServiceDefaults(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[TraceIdHeader] = CurrentTraceId(context);
                return Task.CompletedTask;
            });
            return next(context);
        });

        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0} ms";
            options.GetLevel = RequestLogLevel;

            // The path only: query strings can carry codes, tokens and search terms.
            options.IncludeQueryInRequestPath = false;
            options.EnrichDiagnosticContext = (diagnostics, context) =>
            {
                diagnostics.Set("RequestHost", context.Request.Host.Value ?? string.Empty);
                diagnostics.Set("Endpoint", context.GetEndpoint()?.DisplayName ?? "(none)");
            };
        });
        return app;
    }

    /// <summary>Maps <c>/health/live</c> and <c>/health/ready</c>. Both are anonymous and return no exception details.</summary>
    /// <param name="app">The application.</param>
    /// <returns>The same application.</returns>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapHealthChecks(LivePath, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(LiveTag),
            ResponseWriter = HealthResponseWriter.WriteAsync,
        }).AllowAnonymous();
        app.MapHealthChecks(ReadyPath, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = HealthResponseWriter.WriteAsync,
        }).AllowAnonymous();
        return app;
    }

    private static LogEventLevel RequestLogLevel(HttpContext context, double elapsedMs, Exception? exception)
    {
        if (exception is not null || context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            return LogEventLevel.Error;
        }

        // Health probes run every few seconds; keep them out of normal logs.
        return context.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose : LogEventLevel.Information;
    }

    /// <summary>Returns the W3C trace id of the current request, or the ASP.NET Core trace identifier if none.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The trace id.</returns>
    public static string CurrentTraceId(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    }
}
