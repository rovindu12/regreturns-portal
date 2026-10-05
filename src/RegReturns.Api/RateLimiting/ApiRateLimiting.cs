using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

using RegReturns.Api.Authentication;
using RegReturns.Api.Hosting;
using RegReturns.Api.Problems;

namespace RegReturns.Api.RateLimiting;

/// <summary>The API's rate limit (configuration section <c>Api:RateLimit</c>, ADR 0027).</summary>
public sealed class ApiRateLimitOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Api:RateLimit";

    /// <summary>Gets or sets how many requests a client may make per window.</summary>
    [Range(1, 100_000)]
    public int PermitLimit { get; set; } = 120;

    /// <summary>Gets or sets the window length in seconds.</summary>
    [Range(1, 3600)]
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// A fixed window per client id on the API's controllers (ADR 0027). Callers without a client id (no or bad token)
/// share a window per IP address, so they cannot flood the API either. A refusal is a 429 problem with
/// <c>Retry-After</c>, logged and counted.
/// </summary>
internal static partial class ApiRateLimiting
{
    /// <summary>The policy controllers are mapped with.</summary>
    public const string PolicyName = "api-client";

    /// <summary>Adds the rate limiter and its options.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ApiRateLimitOptions>()
            .Bind(configuration.GetSection(ApiRateLimitOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(PolicyName, context =>
            {
                var settings = context.RequestServices.GetRequiredService<IOptions<ApiRateLimitOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.PermitLimit,
                    Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
            });
            options.OnRejected = OnRejectedAsync;
        });
        return services;
    }

    /// <summary>Returns the partition of a request: its client id, or its IP address when it has none.</summary>
    /// <param name="context">The request.</param>
    /// <returns>The partition key.</returns>
    internal static string PartitionKey(HttpContext context) =>
        InstitutionClaimsTransformation.ClientIdOf(context.User) is { } clientId
            ? string.Concat("client:", clientId)
            : string.Concat("ip:", context.Connection.RemoteIpAddress?.ToString() ?? "unknown");

    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        var settings = http.RequestServices.GetRequiredService<IOptions<ApiRateLimitOptions>>().Value;
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
            ? Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds))
            : settings.WindowSeconds;
        http.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);

        var logger = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ApiRateLimiting).FullName!);
        LogRateLimited(logger, PartitionKey(http), settings.PermitLimit, settings.WindowSeconds, retryAfter);
        ApiTelemetry.RateLimited.Add(1);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many requests.",
            Detail = string.Create(
                CultureInfo.InvariantCulture,
                $"The client may make {settings.PermitLimit} requests every {settings.WindowSeconds} seconds. Retry after {retryAfter} seconds."),
            Extensions = { [ApiProblems.CodeMember] = ApiProblems.RateLimitedCode },
        };
        await http.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = problem,
        });
    }

    [LoggerMessage(EventId = 3304, Level = LogLevel.Warning,
        Message = "Rate limit reached for {Partition} ({PermitLimit} requests per {WindowSeconds} s); retry after {RetryAfterSeconds} s")]
    private static partial void LogRateLimited(ILogger logger, string partition, int permitLimit, int windowSeconds, int retryAfterSeconds);
}
