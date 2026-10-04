using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

using RegReturns.Infrastructure;
using RegReturns.Infrastructure.Identity.Authorization;

namespace RegReturns.Api.Authentication;

/// <summary>Registers the API's identity pipeline: WSO2 bearer tokens, institution scoping, policies and auditing.</summary>
internal static class ApiAuthenticationExtensions
{
    /// <summary>
    /// Adds the WSO2 back channel, the audit trail, the RegReturns policies, JWT bearer authentication as the default
    /// scheme and the institution claims transformation. Requires <c>Wso2:Authority</c> and <c>Audit:HmacKey</c>;
    /// the API refuses to start without them.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddWso2Backchannel(configuration);
        services.AddAuditTrail(configuration);
        services.AddRegReturnsAuthorization(configuration);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureApiJwtBearerOptions>();
        services.AddScoped<ApiJwtBearerEvents>();

        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddScoped<IClaimsTransformation, InstitutionClaimsTransformation>();
        return services;
    }
}
