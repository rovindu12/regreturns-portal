using System.Buffers.Text;
using System.Security.Cryptography;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace RegReturns.ServiceDefaults.Web;

/// <summary>
/// Security response headers shared by the portal and the API (ADR 0033): a Content Security Policy chosen per
/// response, so a page can carry a fresh script nonce, and fixed headers against sniffing, framing, referrer leaks and
/// cross-origin reads. Signed-in responses are not stored by browsers or proxies unless the endpoint says otherwise.
/// HSTS stays with ASP.NET Core's <c>UseHsts</c>, which skips localhost.
/// </summary>
public static class SecurityHeaders
{
    /// <summary>The features a page may never use (cameras, payment, topics and the like).</summary>
    public const string PermissionsPolicy =
        "accelerometer=(), browsing-topics=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";

    private const int NonceBytes = 16;
    private static readonly object NonceKey = new();

    /// <summary>
    /// Returns the request's Content Security Policy nonce, created on first use: 128 random bits in URL-safe Base64
    /// (which CSP allows), so HTML encoding leaves it unchanged in the page. Every <c>&lt;script&gt;</c> the portal
    /// renders carries it, and the policy allows no other script.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The nonce, the same for the whole request.</returns>
    public static string CspNonce(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Items[NonceKey] is not string nonce)
        {
            nonce = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(NonceBytes));
            context.Items[NonceKey] = nonce;
        }

        return nonce;
    }

    /// <summary>Adds the security headers to every response, including errors and static files.</summary>
    /// <param name="app">The application.</param>
    /// <param name="contentSecurityPolicy">Returns the policy for a response.</param>
    /// <param name="referrerPolicy">The <c>Referrer-Policy</c> value.</param>
    /// <returns>The same application.</returns>
    public static IApplicationBuilder UseSecurityHeaders(
        this IApplicationBuilder app, Func<HttpContext, string> contentSecurityPolicy, string referrerPolicy)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(contentSecurityPolicy);
        ArgumentException.ThrowIfNullOrWhiteSpace(referrerPolicy);
        return app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                Apply(context, contentSecurityPolicy(context), referrerPolicy);
                return Task.CompletedTask;
            });
            return next(context);
        });
    }

    /// <summary>Sets the headers on a response that is about to start.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="contentSecurityPolicy">The policy for this response.</param>
    /// <param name="referrerPolicy">The <c>Referrer-Policy</c> value.</param>
    internal static void Apply(HttpContext context, string contentSecurityPolicy, string referrerPolicy)
    {
        var headers = context.Response.Headers;
        headers.ContentSecurityPolicy = contentSecurityPolicy;
        headers.XContentTypeOptions = "nosniff";

        // Superseded by frame-ancestors, kept for browsers that predate it.
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = referrerPolicy;
        headers["Permissions-Policy"] = PermissionsPolicy;
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Cross-Origin-Resource-Policy"] = "same-origin";
        headers.Remove(HeaderNames.Server);
        headers.Remove(HeaderNames.XPoweredBy);

        // Pages and answers for a signed-in user hold bank figures: never keep them in a browser or proxy cache.
        if (context.User.Identity?.IsAuthenticated == true && string.IsNullOrEmpty(headers.CacheControl))
        {
            headers.CacheControl = "no-store";
        }
    }
}
