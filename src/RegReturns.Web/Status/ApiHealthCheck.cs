using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace RegReturns.Web.Status;

/// <summary>
/// Reports whether the REST API answers its own readiness check (database and WSO2), for the status page only: the
/// portal keeps working without the API, so this check is not part of the portal's readiness.
/// </summary>
/// <param name="httpClientFactory">Creates the client.</param>
/// <param name="options">Where the API's readiness endpoint is.</param>
internal sealed class ApiHealthCheck(IHttpClientFactory httpClientFactory, IOptions<StatusOptions> options) : IHealthCheck
{
    /// <summary>The name of the <see cref="HttpClient"/> the check uses.</summary>
    public const string HttpClientName = "RegReturns.Status.Api";

    /// <summary>How long the API has to answer.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        try
        {
            using var response = await client.GetAsync(options.Value.ApiHealthUrl, cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("The API is ready.")
                : HealthCheckResult.Unhealthy($"The API answered its readiness check with {(int)response.StatusCode}.");
        }
        catch (HttpRequestException ex)
        {
            return HealthCheckResult.Unhealthy("The API is not reachable.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("The API did not answer in time.", ex);
        }
    }
}
