using Microsoft.Extensions.Options;

using RegReturns.Infrastructure.Identity.Wso2;
using RegReturns.ServiceDefaults.Web;

namespace RegReturns.Web.Security;

/// <summary>
/// The portal's Content Security Policy (ADR 0033). Scripts run only with the request's nonce, which
/// <see cref="CspNonceTagHelper"/> puts on every <c>&lt;script&gt;</c> the views render, so an injected script never
/// runs, even one from this origin. Everything else comes from the portal itself; forms may also lead to WSO2
/// (sign-out posts here and is redirected there); no page may be framed.
/// </summary>
public static class PortalSecurityHeaders
{
    /// <summary>Referrers are sent only to the portal itself: URLs carry return ids.</summary>
    public const string ReferrerPolicy = "same-origin";

    /// <summary>Builds the policy for a response.</summary>
    /// <param name="nonce">The request's script nonce.</param>
    /// <param name="identityProvider">The origin of WSO2's public endpoints.</param>
    /// <returns>The policy.</returns>
    public static string ContentSecurityPolicy(string nonce, string identityProvider) =>
        $"default-src 'self'; script-src 'nonce-{nonce}'; style-src 'self'; img-src 'self' data:; font-src 'self'; " +
        $"connect-src 'self'; object-src 'none'; base-uri 'none'; form-action 'self' {identityProvider}; " +
        "frame-ancestors 'none'; upgrade-insecure-requests";

    /// <summary>Adds the security headers with the portal's policy.</summary>
    /// <param name="app">The application.</param>
    /// <returns>The same application.</returns>
    public static WebApplication UsePortalSecurityHeaders(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var authority = app.Services.GetRequiredService<IOptions<Wso2Options>>().Value.Authority
            ?? throw new InvalidOperationException("Wso2:Authority is not configured.");
        var identityProvider = authority.GetLeftPart(UriPartial.Authority);
        app.UseSecurityHeaders(context => ContentSecurityPolicy(context.CspNonce(), identityProvider), ReferrerPolicy);
        return app;
    }
}
