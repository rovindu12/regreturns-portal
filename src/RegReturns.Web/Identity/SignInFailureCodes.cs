using System.Collections.Frozen;

using Microsoft.AspNetCore.Authentication;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace RegReturns.Web.Identity;

/// <summary>
/// Short, stable codes for failed sign-ins. They go into the audit trail and the failure page's query string, so they
/// never contain exception messages or anything a caller controls beyond a known OAuth error code.
/// </summary>
public static class SignInFailureCodes
{
    /// <summary>Any failure without a more specific code.</summary>
    public const string Generic = "sign_in_failed";

    /// <summary>The correlation or state check failed: the sign-in took too long, was replayed or started in another tab.</summary>
    public const string RequestExpired = "request_expired";

    /// <summary>WSO2's discovery document, keys or token endpoint could not be reached.</summary>
    public const string IdentityProviderUnreachable = "idp_unreachable";

    /// <summary>The id_token failed validation (issuer, audience, signature, lifetime or nonce).</summary>
    public const string TokenInvalid = "id_token_invalid";

    /// <summary>WSO2 returned an OAuth error that is not in the known list.</summary>
    public const string ProtocolError = "oidc_error";

    /// <summary>The user cancelled or WSO2 refused the sign-in.</summary>
    public const string AccessDenied = "access_denied";

    /// <summary>WSO2 reported an internal error (OAuth <c>server_error</c>).</summary>
    public const string ServerError = "server_error";

    /// <summary>WSO2 is overloaded or in maintenance (OAuth <c>temporarily_unavailable</c>).</summary>
    public const string TemporarilyUnavailable = "temporarily_unavailable";

    /// <summary>The error message prefix of a failed discovery-document fetch in Microsoft.IdentityModel.</summary>
    private const string ConfigurationUnavailablePrefix = "IDX20803";

    /// <summary>OAuth 2.0 and OIDC error codes passed through as they are (RFC 6749 §4.1.2.1, OIDC Core §3.1.2.6).</summary>
    private static readonly FrozenSet<string> KnownProtocolErrors = new[]
    {
        AccessDenied, ServerError, TemporarilyUnavailable, "login_required", "consent_required", "interaction_required",
        "invalid_request", "invalid_client", "invalid_grant", "invalid_scope", "unauthorized_client", "unsupported_response_type",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Maps a remote authentication failure to a code.</summary>
    /// <param name="failure">The failure reported by the OpenID Connect handler.</param>
    /// <returns>The code.</returns>
    public static string From(Exception? failure) => failure switch
    {
        SignInRejectedException rejected => rejected.Code,
        OpenIdConnectProtocolException protocol when protocol.Data["error"] is string error && KnownProtocolErrors.Contains(error) => error,
        OpenIdConnectProtocolException => ProtocolError,
        SecurityTokenException => TokenInvalid,
        HttpRequestException => IdentityProviderUnreachable,
        InvalidOperationException invalid when invalid.Message.StartsWith(ConfigurationUnavailablePrefix, StringComparison.Ordinal) =>
            IdentityProviderUnreachable,
        AuthenticationFailureException => RequestExpired,
        _ => Generic,
    };
}
