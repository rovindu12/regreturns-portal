using System.ComponentModel.DataAnnotations;

namespace RegReturns.Web.Identity;

/// <summary>
/// The portal's OpenID Connect client registration in WSO2 (section <c>Oidc</c>). The client secret comes only from
/// user-secrets or the <c>Oidc__ClientSecret</c> environment variable, never from appsettings.
/// </summary>
public sealed class OidcOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Oidc";

    /// <summary>Gets or sets the OAuth client id of the portal application in WSO2.</summary>
    [Required]
    public string? ClientId { get; set; }

    /// <summary>Gets or sets the OAuth client secret of the portal application in WSO2.</summary>
    [Required]
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the authorization request is sent through pushed authorization
    /// (RFC 9126) when WSO2 advertises it. Off by default: the plain code flow with PKCE is the verified path.
    /// </summary>
    public bool UsePushedAuthorization { get; set; }
}
