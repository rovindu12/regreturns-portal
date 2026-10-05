using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using RegReturns.Application.Identity;
using RegReturns.Infrastructure.Identity.Wso2;

namespace RegReturns.Api.Authentication;

/// <summary>
/// Configures the bearer scheme for WSO2 client-credentials access tokens (plan §4.4, RFC 9068 and RFC 8725):
/// issuer, the API audience, RS256 only, the <c>at+jwt</c> type, a 30-second clock skew, and signing keys fetched
/// through the WSO2 back channel, which trusts only the configured CA. The framework's configuration manager caches
/// the keys and refreshes them automatically and whenever a token names an unknown key.
/// </summary>
/// <param name="wso2">The WSO2 options.</param>
/// <param name="httpClientFactory">Creates the WSO2 back-channel client.</param>
internal sealed class ConfigureApiJwtBearerOptions(IOptions<Wso2Options> wso2, IHttpClientFactory httpClientFactory)
    : IConfigureNamedOptions<JwtBearerOptions>
{
    /// <summary>The <c>typ</c> header of a JWT access token (RFC 9068 §2.1).</summary>
    public const string AccessTokenType = "at+jwt";

    /// <summary>The full media type form of <see cref="AccessTokenType"/>, which RFC 9068 §4 also requires accepting.</summary>
    public const string AccessTokenMediaType = "application/at+jwt";

    /// <summary>The only accepted signing algorithm.</summary>
    public const string SigningAlgorithm = SecurityAlgorithms.RsaSha256;

    /// <summary>How far token lifetimes may disagree with this server's clock.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    /// <summary>Upper bound for a discovery or JWKS response.</summary>
    private const int MaxMetadataResponseBytes = 1024 * 1024;

    /// <inheritdoc />
    public void Configure(string? name, JwtBearerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!string.Equals(name, JwtBearerDefaults.AuthenticationScheme, StringComparison.Ordinal))
        {
            return;
        }

        var settings = wso2.Value;
        options.MapInboundClaims = false;
        options.SaveToken = false;
        options.RequireHttpsMetadata = true;
        options.MetadataAddress = settings.MetadataAddress.AbsoluteUri;

        // JwtBearerPostConfigureOptions builds the configuration manager on this client, so discovery and JWKS
        // go through the CA-pinned, URL-rewriting pipeline instead of a default HttpClient.
        var backchannel = httpClientFactory.CreateClient(Wso2Backchannel.HttpClientName);
        backchannel.Timeout = options.BackchannelTimeout;
        backchannel.MaxResponseContentBufferSize = MaxMetadataResponseBytes;
        options.Backchannel = backchannel;

        options.EventsType = typeof(ApiJwtBearerEvents);

        // WWW-Authenticate says only "invalid_token"; why a token failed goes to the logs and the audit trail.
        options.IncludeErrorDetails = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = settings.Issuer.AbsoluteUri,
            ValidAudience = ApiScopes.ApiIdentifier,

            // Portal id_tokens also carry the API audience; only access tokens are typed at+jwt.
            ValidTypes = [AccessTokenType, AccessTokenMediaType],
            ValidAlgorithms = [SigningAlgorithm],
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireAudience = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ClockSkew = ClockSkew,
            NameClaimType = ClaimNames.Subject,
            RoleClaimType = ClaimNames.Roles,
        };
    }

    /// <inheritdoc />
    public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);
}
