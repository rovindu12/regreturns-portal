using System.Globalization;

using Microsoft.AspNetCore.Authentication;

namespace RegReturns.Web.Identity;

/// <summary>
/// The portal session cookie: 20 minutes sliding, never longer than 8 hours after sign-in (plan §4.3).
/// Sliding renewal re-issues the cookie with a new <see cref="AuthenticationProperties.IssuedUtc"/>, so the absolute
/// limit is stamped into the authentication properties once, at sign-in, and checked on every request.
/// </summary>
public static class PortalSession
{
    /// <summary>The session cookie name; the <c>__Host-</c> prefix makes browsers require Secure, path <c>/</c> and no domain.</summary>
    public const string CookieName = "__Host-RegReturns";

    /// <summary>The antiforgery cookie name.</summary>
    public const string AntiforgeryCookieName = "__Host-RegReturns-Antiforgery";

    /// <summary>Authentication property holding the absolute expiry (ISO 8601, UTC).</summary>
    public const string AbsoluteExpiryKey = ".regreturns.absolute_expires";

    /// <summary>Gets how long a session may be idle before it ends.</summary>
    public static TimeSpan IdleTimeout { get; } = TimeSpan.FromMinutes(20);

    /// <summary>Gets the maximum session length, however active the user is.</summary>
    public static TimeSpan AbsoluteLifetime { get; } = TimeSpan.FromHours(8);

    /// <summary>Stamps the absolute expiry into the authentication properties.</summary>
    /// <param name="properties">The properties of the ticket being issued.</param>
    /// <param name="expiresAt">When the session must end.</param>
    public static void SetAbsoluteExpiry(AuthenticationProperties properties, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(properties);
        properties.Items[AbsoluteExpiryKey] = expiresAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    /// <summary>Reads the absolute expiry from the authentication properties.</summary>
    /// <param name="properties">The ticket's properties.</param>
    /// <returns>The absolute expiry, or <see langword="null"/> if it is missing or unreadable.</returns>
    public static DateTimeOffset? GetAbsoluteExpiry(AuthenticationProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return properties.Items.TryGetValue(AbsoluteExpiryKey, out var raw) &&
            DateTimeOffset.TryParseExact(raw, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiresAt)
            ? expiresAt
            : null;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the session has reached its absolute expiry. A ticket without one was not
    /// issued by the portal's sign-in and counts as expired.
    /// </summary>
    /// <param name="properties">The ticket's properties.</param>
    /// <param name="now">The current time.</param>
    /// <returns>Whether the session must end.</returns>
    public static bool HasExpired(AuthenticationProperties properties, DateTimeOffset now) =>
        GetAbsoluteExpiry(properties) is not { } expiresAt || now >= expiresAt;
}
