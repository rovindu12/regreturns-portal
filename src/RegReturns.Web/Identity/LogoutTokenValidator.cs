using System.Text.Json;

using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

using RegReturns.Application.Identity;
using RegReturns.Infrastructure.Identity.Wso2;

namespace RegReturns.Web.Identity;

/// <summary>
/// Validates OpenID Connect back-channel logout tokens (OIDC Back-Channel Logout 1.0 §2.6): a JWT signed with one of
/// WSO2's keys (taken from the OIDC handler's configuration manager, so key rotation is shared), issued by the WSO2
/// issuer to the portal's client id, recently issued, carrying the back-channel logout event and a <c>sid</c>, and
/// without a <c>nonce</c> (so an id_token cannot be replayed as a logout token).
/// </summary>
/// <param name="openIdConnectOptions">The OIDC handler options, whose configuration manager holds WSO2's keys.</param>
/// <param name="wso2Options">Where WSO2 is.</param>
/// <param name="oidcOptions">The portal's client registration.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed partial class LogoutTokenValidator(
    IOptionsMonitor<OpenIdConnectOptions> openIdConnectOptions,
    IOptions<Wso2Options> wso2Options,
    IOptions<OidcOptions> oidcOptions,
    TimeProvider timeProvider,
    ILogger<LogoutTokenValidator> logger)
{
    /// <summary>The member of the <c>events</c> claim that identifies a back-channel logout token.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "S5332:Using clear-text protocols is security-sensitive",
        Justification = "A fixed event identifier defined by OIDC Back-Channel Logout 1.0, compared as a string; nothing is fetched from it.")]
    public const string BackchannelLogoutEvent = "http://schemas.openid.net/event/backchannel-logout";

    /// <summary>The claim holding the security events.</summary>
    public const string EventsClaim = "events";

    /// <summary>The longest logout token accepted, in characters.</summary>
    public const int MaxTokenLength = 8 * 1024;

    private static readonly string[] AllowedAlgorithms =
    [
        SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSha384, SecurityAlgorithms.RsaSha512,
        SecurityAlgorithms.RsaSsaPssSha256, SecurityAlgorithms.RsaSsaPssSha384, SecurityAlgorithms.RsaSsaPssSha512,
        SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.EcdsaSha384, SecurityAlgorithms.EcdsaSha512,
    ];

    private readonly JsonWebTokenHandler _handler = new() { MapInboundClaims = false };

    /// <summary>Gets how old a logout token may be (by <c>iat</c>); logout tokens are delivered as soon as they are made.</summary>
    public static TimeSpan MaxTokenAge { get; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets the tolerated clock difference between WSO2 and the portal.</summary>
    public static TimeSpan ClockSkew { get; } = TimeSpan.FromMinutes(2);

    /// <summary>Validates a logout token.</summary>
    /// <param name="logoutToken">The <c>logout_token</c> form field.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The session to end, or why the token was rejected.</returns>
    public async Task<LogoutTokenValidationResult> ValidateAsync(string? logoutToken, CancellationToken cancellationToken)
    {
        // Cheap checks first, so garbage never triggers a fetch of WSO2's keys.
        if (string.IsNullOrWhiteSpace(logoutToken) || logoutToken.Length > MaxTokenLength || !_handler.CanReadToken(logoutToken))
        {
            return LogoutTokenValidationResult.Failure(LogoutTokenFailures.Malformed);
        }

        var options = openIdConnectOptions.Get(OpenIdConnectDefaults.AuthenticationScheme);
        try
        {
            var result = await _handler.ValidateTokenAsync(logoutToken, await ParametersAsync(options, cancellationToken));
            if (result.Exception is SecurityTokenSignatureKeyNotFoundException && options.ConfigurationManager is not null)
            {
                // WSO2 may have rotated its signing key; the configuration manager rate-limits refreshes.
                options.ConfigurationManager.RequestRefresh();
                result = await _handler.ValidateTokenAsync(logoutToken, await ParametersAsync(options, cancellationToken));
            }

            return result.IsValid && result.SecurityToken is JsonWebToken token
                ? CheckLogoutClaims(token)
                : LogoutTokenValidationResult.Failure(Classify(result.Exception));
        }
        catch (InvalidOperationException ex)
        {
            // The configuration manager throws this when discovery or JWKS cannot be fetched.
            LogSigningKeysUnavailable(logger, ex);
            return LogoutTokenValidationResult.Failure(LogoutTokenFailures.ConfigurationUnavailable);
        }
    }

    private async Task<TokenValidationParameters> ParametersAsync(OpenIdConnectOptions options, CancellationToken cancellationToken)
    {
        var configuration = options.Configuration
            ?? await (options.ConfigurationManager
                ?? throw new InvalidOperationException("The OpenID Connect handler has no configuration manager."))
                .GetConfigurationAsync(cancellationToken);

        return new TokenValidationParameters
        {
            ValidIssuer = wso2Options.Value.Issuer.AbsoluteUri,
            ValidAudience = oidcOptions.Value.ClientId,
            IssuerSigningKeys = configuration.SigningKeys,
            ValidAlgorithms = AllowedAlgorithms,
            RequireSignedTokens = true,

            // exp is optional in logout tokens from older providers; when present it is checked against the
            // injected clock, and the token's age is always bounded by iat in CheckLogoutClaims.
            ValidateLifetime = true,
            RequireExpirationTime = false,
            LifetimeValidator = IsWithinLifetime,
        };
    }

    private bool IsWithinLifetime(DateTime? notBefore, DateTime? expires, SecurityToken token, TokenValidationParameters parameters)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return (notBefore is null || notBefore.Value <= now + ClockSkew) && (expires is null || now < expires.Value + ClockSkew);
    }

    private LogoutTokenValidationResult CheckLogoutClaims(JsonWebToken token)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (!token.TryGetClaim(JwtRegisteredClaimNames.Iat, out _) ||
            token.IssuedAt > now + ClockSkew ||
            token.IssuedAt < now - MaxTokenAge)
        {
            return LogoutTokenValidationResult.Failure(LogoutTokenFailures.Lifetime);
        }

        if (!HasBackchannelLogoutEvent(token))
        {
            return LogoutTokenValidationResult.Failure(LogoutTokenFailures.MissingEvent);
        }

        if (token.TryGetClaim(JwtRegisteredClaimNames.Nonce, out _))
        {
            return LogoutTokenValidationResult.Failure(LogoutTokenFailures.NoncePresent);
        }

        if (!token.TryGetPayloadValue<string>(ClaimNames.SessionId, out var sessionId) || string.IsNullOrWhiteSpace(sessionId))
        {
            return LogoutTokenValidationResult.Failure(LogoutTokenFailures.MissingSessionId);
        }

        return LogoutTokenValidationResult.Success(sessionId, string.IsNullOrWhiteSpace(token.Subject) ? null : token.Subject);
    }

    private static bool HasBackchannelLogoutEvent(JsonWebToken token)
    {
        if (!token.TryGetClaim(EventsClaim, out var events))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(events.Value);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty(BackchannelLogoutEvent, out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Classify(Exception? exception) => exception switch
    {
        SecurityTokenInvalidIssuerException => LogoutTokenFailures.Issuer,
        SecurityTokenInvalidAudienceException => LogoutTokenFailures.Audience,
        SecurityTokenInvalidLifetimeException or SecurityTokenExpiredException or SecurityTokenNotYetValidException => LogoutTokenFailures.Lifetime,
        SecurityTokenInvalidSignatureException or SecurityTokenInvalidAlgorithmException or SecurityTokenSignatureKeyNotFoundException => LogoutTokenFailures.Signature,
        SecurityTokenMalformedException => LogoutTokenFailures.Malformed,
        _ => LogoutTokenFailures.Invalid,
    };

    [LoggerMessage(EventId = 3109, Level = LogLevel.Error, Message = "WSO2 signing keys could not be loaded to validate a logout token")]
    private static partial void LogSigningKeysUnavailable(ILogger logger, Exception exception);
}

/// <summary>The outcome of <see cref="LogoutTokenValidator.ValidateAsync"/>.</summary>
/// <param name="SessionId">The WSO2 session to end, when valid.</param>
/// <param name="Subject">The WSO2 user id (<c>sub</c>), when the token carries one.</param>
/// <param name="FailureReason">A <see cref="LogoutTokenFailures"/> code, when invalid.</param>
public sealed record LogoutTokenValidationResult(string? SessionId, string? Subject, string? FailureReason)
{
    /// <summary>Gets a value indicating whether the token is valid.</summary>
    public bool IsValid => FailureReason is null && SessionId is not null;

    /// <summary>Creates a successful result.</summary>
    /// <param name="sessionId">The WSO2 session id.</param>
    /// <param name="subject">The WSO2 user id, if present.</param>
    /// <returns>The result.</returns>
    public static LogoutTokenValidationResult Success(string sessionId, string? subject) => new(sessionId, subject, null);

    /// <summary>Creates a failed result.</summary>
    /// <param name="reason">A <see cref="LogoutTokenFailures"/> code.</param>
    /// <returns>The result.</returns>
    public static LogoutTokenValidationResult Failure(string reason) => new(null, null, reason);
}

/// <summary>Why a logout token was rejected; safe to log.</summary>
public static class LogoutTokenFailures
{
    /// <summary>Missing, too long or not a JWT.</summary>
    public const string Malformed = "malformed";

    /// <summary>WSO2's discovery document or keys could not be loaded.</summary>
    public const string ConfigurationUnavailable = "configuration_unavailable";

    /// <summary>Not signed by a current WSO2 key with an allowed algorithm.</summary>
    public const string Signature = "signature";

    /// <summary>Issued by someone other than WSO2.</summary>
    public const string Issuer = "issuer";

    /// <summary>Not addressed to the portal's client id.</summary>
    public const string Audience = "audience";

    /// <summary>Expired, not yet valid, without <c>iat</c>, or too old.</summary>
    public const string Lifetime = "lifetime";

    /// <summary>No back-channel logout event in <c>events</c>.</summary>
    public const string MissingEvent = "events";

    /// <summary>Carries a <c>nonce</c>, which logout tokens must not.</summary>
    public const string NoncePresent = "nonce";

    /// <summary>No <c>sid</c>: the portal ends sessions by WSO2 session id only.</summary>
    public const string MissingSessionId = "sid";

    /// <summary>Any other validation failure.</summary>
    public const string Invalid = "invalid";
}
