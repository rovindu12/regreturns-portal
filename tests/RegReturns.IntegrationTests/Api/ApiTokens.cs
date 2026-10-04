using System.Security.Cryptography;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

using RegReturns.Application.Identity;

namespace RegReturns.IntegrationTests.Api;

/// <summary>
/// Issues access tokens shaped like WSO2's client-credentials tokens, signed with a locally generated key that the
/// API under test trusts through a static OpenID Connect configuration instead of WSO2's discovery and JWKS.
/// </summary>
public static class ApiTokens
{
    /// <summary>The WSO2 authority configured in the API under test.</summary>
    public const string Authority = "https://iam.test.invalid/";

    /// <summary>The issuer WSO2 would put in tokens for <see cref="Authority"/>.</summary>
    public const string Issuer = Authority + "oauth2/token";

    /// <summary>WSO2's <c>typ</c> header for access tokens.</summary>
    public const string AccessTokenType = "at+jwt";

    /// <summary>WSO2's access token lifetime.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(300);

    private static readonly RSA TrustedRsa = RSA.Create(2048);
    private static readonly RSA UntrustedRsa = RSA.Create(2048);

    /// <summary>Gets the RSA key published in <see cref="Configuration"/>.</summary>
    public static RsaSecurityKey TrustedKey { get; } = new(TrustedRsa) { KeyId = "trusted-rsa" };

    /// <summary>Gets an RSA key the API does not know.</summary>
    public static RsaSecurityKey UntrustedKey { get; } = new(UntrustedRsa) { KeyId = "untrusted-rsa" };

    /// <summary>
    /// Gets a symmetric key that is also published in <see cref="Configuration"/>, so a test can show that HS256 is
    /// refused by the algorithm allow-list even when the key itself is known.
    /// </summary>
    public static SymmetricSecurityKey TrustedSymmetricKey { get; } = new(RandomNumberGenerator.GetBytes(32)) { KeyId = "trusted-hmac" };

    /// <summary>Gets the static configuration given to the API's bearer scheme.</summary>
    public static OpenIdConnectConfiguration Configuration { get; } = CreateConfiguration();

    /// <summary>Creates a signed token; <paramref name="customise"/> changes the defaults of a valid one.</summary>
    public static string Create(string clientId, Action<AccessTokenSpec>? customise = null)
    {
        var spec = new AccessTokenSpec();
        customise?.Invoke(spec);

        var expires = TimeProvider.System.GetUtcNow() + spec.ExpiresIn;
        var issuedAt = expires - Lifetime;
        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [JwtRegisteredClaimNames.Aud] = new[] { clientId, spec.Audience },
            [ClaimNames.Subject] = clientId,
            [ClaimNames.AuthorizedUserType] = ClaimNames.ApplicationTokenType,
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
        };
        if (spec.IncludeClientId)
        {
            claims[ClaimNames.AuthorizedParty] = clientId;
            claims[ClaimNames.ClientId] = clientId;
        }

        if (spec.Scope is not null)
        {
            claims[ClaimNames.Scope] = spec.Scope;
        }

        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = spec.Issuer,
            TokenType = spec.Type,
            Claims = claims,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = spec.SigningCredentials,
        });
    }

    private static OpenIdConnectConfiguration CreateConfiguration()
    {
        var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
        configuration.SigningKeys.Add(TrustedKey);
        configuration.SigningKeys.Add(TrustedSymmetricKey);
        return configuration;
    }
}

/// <summary>What goes into a test token. The defaults describe a valid WSO2 client-credentials token.</summary>
public sealed class AccessTokenSpec
{
    /// <summary>Gets or sets the issuer.</summary>
    public string Issuer { get; set; } = ApiTokens.Issuer;

    /// <summary>Gets or sets the audience added next to the client id.</summary>
    public string Audience { get; set; } = ApiScopes.ApiIdentifier;

    /// <summary>Gets or sets the <c>typ</c> header.</summary>
    public string Type { get; set; } = ApiTokens.AccessTokenType;

    /// <summary>Gets or sets the signing key and algorithm.</summary>
    public SigningCredentials SigningCredentials { get; set; } = new(ApiTokens.TrustedKey, SecurityAlgorithms.RsaSha256);

    /// <summary>Gets or sets the space-separated scopes, or <see langword="null"/> for none.</summary>
    public string? Scope { get; set; } = ApiScopes.ReferenceRead;

    /// <summary>Gets or sets a value indicating whether <c>azp</c> and <c>client_id</c> are included.</summary>
    public bool IncludeClientId { get; set; } = true;

    /// <summary>Gets or sets how long from now the token expires; negative for a token that has already expired.</summary>
    public TimeSpan ExpiresIn { get; set; } = ApiTokens.Lifetime;
}
