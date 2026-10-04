using System.ComponentModel.DataAnnotations;

namespace RegReturns.Infrastructure.Identity.Wso2;

/// <summary>Where WSO2 Identity Server is and how its certificate is trusted (section <c>Wso2</c>).</summary>
public sealed class Wso2Options
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Wso2";

    /// <summary>
    /// Gets or sets the public base URL of WSO2 (for example <c>https://localhost:9443</c>). The token issuer and
    /// every discovery URL are derived from it and validated against it.
    /// </summary>
    [Required]
    public Uri? Authority { get; set; }

    /// <summary>
    /// Gets or sets the base URL the apps use to reach WSO2 server-to-server (for example <c>https://wso2:9443</c>
    /// inside Docker). Requests to <see cref="Authority"/> are rewritten to it. Defaults to <see cref="Authority"/>.
    /// </summary>
    public Uri? BackchannelAuthority { get; set; }

    /// <summary>
    /// Gets or sets the path of a PEM file with the CA certificate(s) that issued WSO2's HTTPS certificate.
    /// When set, server-to-server calls trust only these roots. When empty, the operating system's trust store applies.
    /// TLS validation is never switched off.
    /// </summary>
    public string? TrustedCaPath { get; set; }

    /// <summary>Gets the token issuer (<c>iss</c>) WSO2 puts in every token.</summary>
    public Uri Issuer => new(RequireAuthority(), "oauth2/token");

    /// <summary>Gets the OpenID Connect discovery document address.</summary>
    public Uri MetadataAddress => new(RequireAuthority(), "oauth2/token/.well-known/openid-configuration");

    /// <summary>Gets the JSON Web Key Set address.</summary>
    public Uri JwksAddress => new(RequireAuthority(), "oauth2/jwks");

    /// <summary>Gets the base address used for server-to-server calls.</summary>
    public Uri EffectiveBackchannelAuthority => BackchannelAuthority ?? RequireAuthority();

    private Uri RequireAuthority() =>
        Authority ?? throw new InvalidOperationException($"{SectionName}:{nameof(Authority)} is not configured.");
}
