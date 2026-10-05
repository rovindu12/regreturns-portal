using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using RegReturns.Application.Auditing;

namespace RegReturns.Web.Identity;

/// <summary>Registers WSO2 sign-in for the portal: session cookie, OpenID Connect, back-channel logout and auditing.</summary>
public static class PortalAuthenticationExtensions
{
    /// <summary>
    /// Adds the session cookie (default scheme) and OpenID Connect against WSO2 (challenge scheme), the services they
    /// use, the back-channel logout deny list, access-denied auditing, the <see cref="PortalAuditContext"/> that names
    /// the actor of audited data changes, and the <see cref="PortalPolicies"/>. Requires
    /// <c>AddWso2Backchannel</c>, <c>AddAuditTrail</c> and <c>AddRegReturnsAuthorization</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddPortalAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<OidcOptions>()
            .Bind(configuration.GetSection(OidcOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddDistributedMemoryCache();
        services.TryAddSingleton<ISessionDenyList, DistributedSessionDenyList>();
        services.TryAddSingleton<LogoutTokenValidator>();
        services.TryAddScoped<PortalAuditContext>();
        services.AddScoped<IAuditContext>(sp => sp.GetRequiredService<PortalAuditContext>());
        services.TryAddScoped<SignInProcessor>();
        services.TryAddScoped<PortalCookieEvents>();
        services.TryAddScoped<PortalOpenIdConnectEvents>();
        services.AddSingleton<IConfigureOptions<OpenIdConnectOptions>, ConfigureOpenIdConnect>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuditingAuthorizationResultHandler>();
        services.AddAuthorizationBuilder()
            .AddPolicy(PortalPolicies.SignedIn, policy => policy.RequireAuthenticatedUser());

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.Cookie.Name = PortalSession.CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.Path = "/";
                options.Cookie.IsEssential = true;
                options.ExpireTimeSpan = PortalSession.IdleTimeout;
                options.SlidingExpiration = true;
                options.LoginPath = PortalPaths.SignIn;
                options.AccessDeniedPath = PortalPaths.AccessDenied;
                options.EventsType = typeof(PortalCookieEvents);
            })
            .AddOpenIdConnect();

        return services;
    }
}
