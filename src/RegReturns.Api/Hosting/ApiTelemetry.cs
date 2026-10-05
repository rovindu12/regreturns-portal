using System.Diagnostics.Metrics;

using RegReturns.Application.Diagnostics;

namespace RegReturns.Api.Hosting;

/// <summary>Metrics of the API's own plumbing: idempotent requests and rate limiting (ADR 0027).</summary>
internal static class ApiTelemetry
{
    /// <summary>Name of the API meter, collected because it starts with <see cref="RegReturnsTelemetry.Prefix"/>.</summary>
    public const string MeterName = RegReturnsTelemetry.Prefix + ".Api";

    /// <summary>Gets the API meter.</summary>
    public static Meter Meter { get; } = new(MeterName);

    /// <summary>
    /// Gets the count of requests with an idempotency key, tagged with <c>outcome</c>: started, replayed, key_reused or
    /// in_progress.
    /// </summary>
    public static Counter<long> IdempotentRequests { get; } = Meter.CreateCounter<long>(
        "regreturns.api.idempotent_requests", "{request}", "Requests with an Idempotency-Key by outcome.");

    /// <summary>Gets the count of requests refused by the rate limiter.</summary>
    public static Counter<long> RateLimited { get; } = Meter.CreateCounter<long>(
        "regreturns.api.rate_limited", "{request}", "Requests refused because the client exceeded its rate limit.");
}
