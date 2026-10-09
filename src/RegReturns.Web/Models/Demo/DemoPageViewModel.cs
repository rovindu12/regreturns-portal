using RegReturns.Application.Demo;
using RegReturns.Application.Identity;
using RegReturns.Web.Demo;

namespace RegReturns.Web.Models.Demo;

/// <summary>A demo account on the demo page.</summary>
/// <param name="UserName">The user name to sign in with.</param>
/// <param name="DisplayName">The display name.</param>
/// <param name="Bank">The bank's name, for bank staff.</param>
/// <param name="TotpSecret">The published TOTP secret, for accounts that sign in with a second factor.</param>
/// <param name="TotpLink">The <c>otpauth://</c> link of that secret.</param>
/// <param name="TotpQrSvg">The link as a QR code (SVG).</param>
public sealed record DemoAccountCard(
    string UserName, string DisplayName, string? Bank, string? TotpSecret, Uri? TotpLink, string? TotpQrSvg);

/// <summary>The demo accounts of one role.</summary>
/// <param name="Role">What the role is for.</param>
/// <param name="Accounts">Its accounts.</param>
public sealed record DemoRoleGroup(PortalRole Role, IReadOnlyList<DemoAccountCard> Accounts);

/// <summary>The public Swagger demo client.</summary>
/// <param name="ClientId">Its client id.</param>
/// <param name="ClientSecret">Its published secret, if configured.</param>
/// <param name="TokenEndpoint">Where it asks WSO2 for tokens.</param>
/// <param name="SwaggerUrl">The API's Swagger UI, if the API address is configured.</param>
/// <param name="Scopes">The scopes it may ask for.</param>
public sealed record DemoApiClientViewModel(
    string ClientId, string? ClientSecret, Uri? TokenEndpoint, Uri? SwaggerUrl, IReadOnlyList<string> Scopes);

/// <summary>Data for the demo page (ADR 0031).</summary>
/// <param name="Roles">The demo accounts by role, in workflow order.</param>
/// <param name="Password">The password every demo account shares, if configured.</param>
/// <param name="Api">The Swagger demo client, if configured.</param>
/// <param name="Status">Reset history and schedule.</param>
public sealed record DemoPageViewModel(
    IReadOnlyList<DemoRoleGroup> Roles,
    string? Password,
    DemoApiClientViewModel? Api,
    DemoStatus Status)
{
    /// <summary>The demo client's scopes: read-only, so visitors cannot deliver returns through it.</summary>
    public static readonly IReadOnlyList<string> DemoApiScopes = [ApiScopes.ReferenceRead, ApiScopes.ReturnsRead];

    /// <summary>Builds the page from the directory and the published settings.</summary>
    /// <param name="accounts">The demo accounts.</param>
    /// <param name="options">The demo settings.</param>
    /// <param name="tokenEndpoint">WSO2's token endpoint, if WSO2 is configured.</param>
    /// <param name="status">Reset history and schedule.</param>
    /// <returns>The page model.</returns>
    public static DemoPageViewModel Create(
        IReadOnlyList<DemoAccount> accounts, DemoOptions options, Uri? tokenEndpoint, DemoStatus status)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(options);
        var groups = PortalRoles.All
            .Select(role => new DemoRoleGroup(role, [.. accounts.Where(a => a.Role == role.Role).Select(a => Card(a, options))]))
            .Where(g => g.Accounts.Count > 0)
            .ToList();

        DemoApiClientViewModel? api = null;
        if (!string.IsNullOrWhiteSpace(options.ApiClientId))
        {
            var swagger = options.ApiBaseUrl is { } baseUrl ? new Uri(baseUrl, "swagger") : null;
            api = new DemoApiClientViewModel(
                options.ApiClientId, NullIfBlank(options.ApiClientSecret), tokenEndpoint, swagger, DemoApiScopes);
        }

        return new DemoPageViewModel(groups, NullIfBlank(options.UserPassword), api, status);
    }

    private static DemoAccountCard Card(DemoAccount account, DemoOptions options)
    {
        var secret = TotpQrCode.Canonical(options.TotpSecretFor(account.UserName));
        var link = secret is null ? null : TotpQrCode.Link(account.UserName, secret);
        return new DemoAccountCard(
            account.UserName, account.DisplayName, account.InstitutionName, secret, link, link is null ? null : TotpQrCode.Svg(link));
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
