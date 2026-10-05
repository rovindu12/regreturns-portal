using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace RegReturns.Infrastructure.Identity.Wso2;

/// <summary>Reports whether WSO2's discovery document and signing keys can be fetched over the back channel.</summary>
/// <param name="httpClientFactory">Creates the WSO2 back-channel client.</param>
/// <param name="options">The WSO2 options.</param>
internal sealed class Wso2HealthCheck(IHttpClientFactory httpClientFactory, IOptions<Wso2Options> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient(Wso2Backchannel.HttpClientName);
        try
        {
            using var discovery = await client.GetAsync(options.Value.MetadataAddress, cancellationToken);
            using var jwks = await client.GetAsync(options.Value.JwksAddress, cancellationToken);
            return discovery.IsSuccessStatusCode && jwks.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("WSO2 discovery and signing keys are reachable.")
                : HealthCheckResult.Unhealthy(
                    $"WSO2 answered discovery with {(int)discovery.StatusCode} and JWKS with {(int)jwks.StatusCode}.");
        }
        catch (HttpRequestException ex)
        {
            // Includes TLS failures; the troubleshooting guide maps the message to a fix.
            return HealthCheckResult.Unhealthy("WSO2 is not reachable over the back channel.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("WSO2 did not answer in time.", ex);
        }
    }
}
