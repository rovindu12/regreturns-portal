using Microsoft.Extensions.Options;

using RegReturns.Infrastructure.Identity.Wso2;
using RegReturns.ServiceDefaults.Web;

namespace RegReturns.Api.Hosting;

/// <summary>
/// The API's security headers (ADR 0033). Answers are JSON for bank systems, so they allow nothing at all; only
/// Swagger UI under <c>/swagger</c> may load its own files and ask WSO2 for a token from the browser.
/// </summary>
public static class ApiSecurityHeaders
{
    /// <summary>No referrer leaves the API.</summary>
    public const string ReferrerPolicy = "no-referrer";

    /// <summary>The policy of every API answer: no content of any kind, no framing.</summary>
    public const string ApiPolicy = "default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    /// <summary>The path Swagger UI is served under.</summary>
    public const string SwaggerPath = "/swagger";

    /// <summary>
    /// Builds Swagger UI's policy. Its bundle sets styles from script and adds style elements, so styles may be
    /// inline; scripts may not.
    /// </summary>
    /// <param name="identityProvider">The origin of WSO2's public token endpoint.</param>
    /// <returns>The policy.</returns>
    public static string SwaggerPolicy(string identityProvider) =>
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
        $"connect-src 'self' {identityProvider}; object-src 'none'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'";

    /// <summary>Adds the security headers with the API's policies.</summary>
    /// <param name="app">The application.</param>
    /// <returns>The same application.</returns>
    public static WebApplication UseApiSecurityHeaders(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var authority = app.Services.GetRequiredService<IOptions<Wso2Options>>().Value.Authority
            ?? throw new InvalidOperationException("Wso2:Authority is not configured.");
        var swaggerPolicy = SwaggerPolicy(authority.GetLeftPart(UriPartial.Authority));
        app.UseSecurityHeaders(
            context => context.Request.Path.StartsWithSegments(SwaggerPath, StringComparison.OrdinalIgnoreCase) ? swaggerPolicy : ApiPolicy,
            ReferrerPolicy);
        return app;
    }
}
