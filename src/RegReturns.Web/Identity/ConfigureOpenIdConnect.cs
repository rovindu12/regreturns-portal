using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

using RegReturns.Application.Identity;
using RegReturns.Infrastructure.Identity.Wso2;

namespace RegReturns.Web.Identity;

/// <summary>
/// Configures the portal's OpenID Connect client against WSO2 (plan §4.3): authorization code with PKCE, issuer and
/// discovery derived from <see cref="Wso2Options"/>, the CA-pinned WSO2 back channel for every server-to-server call,
/// raw JWT claim names, and no tokens kept except the id_token for logout.
/// </summary>
/// <param name="wso2Options">Where WSO2 is.</param>
/// <param name="oidcOptions">The portal's client registration.</param>
/// <param name="httpClientFactory">Creates the WSO2 back-channel client.</param>
public sealed class ConfigureOpenIdConnect(
    IOptions<Wso2Options> wso2Options,
    IOptions<OidcOptions> oidcOptions,
    IHttpClientFactory httpClientFactory) : IConfigureNamedOptions<OpenIdConnectOptions>
{
    /// <inheritdoc />
    public void Configure(string? name, OpenIdConnectOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!string.Equals(name, OpenIdConnectDefaults.AuthenticationScheme, StringComparison.Ordinal))
        {
            return;
        }

        var wso2 = wso2Options.Value;
        var oidc = oidcOptions.Value;

        options.MetadataAddress = wso2.MetadataAddress.AbsoluteUri;
        options.Backchannel = httpClientFactory.CreateClient(Wso2Backchannel.HttpClientName);
        options.ClientId = oidc.ClientId;
        options.ClientSecret = oidc.ClientSecret;

        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;
        options.PushedAuthorizationBehavior = oidc.UsePushedAuthorization
            ? PushedAuthorizationBehavior.UseIfAvailable
            : PushedAuthorizationBehavior.Disable;
        options.Scope.Clear();
        foreach (var scope in OidcScopes.All)
        {
            options.Scope.Add(scope);
        }

        options.MapInboundClaims = false;
        options.GetClaimsFromUserInfoEndpoint = false;
        options.SaveTokens = false;
        options.DisableTelemetry = true;

        options.CallbackPath = PortalPaths.SignInCallback;
        options.SignedOutCallbackPath = PortalPaths.SignedOutCallback;
        options.SignedOutRedirectUri = PortalPaths.SignedOut;
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.SignOutScheme = CookieAuthenticationDefaults.AuthenticationScheme;

        options.TokenValidationParameters.ValidIssuer = wso2.Issuer.AbsoluteUri;
        options.TokenValidationParameters.ValidAudience = oidc.ClientId;
        options.TokenValidationParameters.NameClaimType = ClaimNames.Name;
        options.TokenValidationParameters.RoleClaimType = ClaimNames.Roles;

        options.EventsType = typeof(PortalOpenIdConnectEvents);
    }

    /// <inheritdoc />
    public void Configure(OpenIdConnectOptions options) => Configure(Options.DefaultName, options);
}
