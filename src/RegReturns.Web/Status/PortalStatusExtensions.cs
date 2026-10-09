using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace RegReturns.Web.Status;

/// <summary>Registers the public status page's services.</summary>
public static class PortalStatusExtensions
{
    /// <summary>
    /// Adds <see cref="PortalStatus"/> and, when <c>Status:ApiHealthUrl</c> is set, a check of the REST API tagged
    /// <see cref="PortalStatus.StatusTag"/>: shown on the status page, never part of the portal's own readiness.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The same services.</returns>
    public static IServiceCollection AddPortalStatus(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(StatusOptions.SectionName);
        services.AddSingleton<PortalStatus>();
        services.AddOptions<StatusOptions>()
            .Bind(section)
            .Validate(o => o.AreValid(), $"{StatusOptions.SectionName}:{nameof(StatusOptions.ApiHealthUrl)} must be an absolute http or https URL.")
            .ValidateOnStart();

        if (!string.IsNullOrWhiteSpace(section[nameof(StatusOptions.ApiHealthUrl)]))
        {
            services.AddHttpClient(ApiHealthCheck.HttpClientName, client => client.Timeout = ApiHealthCheck.Timeout);
            services.AddHealthChecks().AddCheck<ApiHealthCheck>(PortalStatus.ApiCheck, HealthStatus.Unhealthy, [PortalStatus.StatusTag]);
        }

        return services;
    }
}
