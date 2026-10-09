using System.ComponentModel.DataAnnotations;

namespace RegReturns.IamBootstrap;

/// <summary>Settings for the IAM setup tool (section <c>IamBootstrap</c>).</summary>
internal sealed class BootstrapOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "IamBootstrap";

    /// <summary>Minimum length of the shared demo user password.</summary>
    public const int MinimumDemoPasswordLength = 12;

    private const char PathSeparator = '/';

    /// <summary>Gets or sets the WSO2 super admin user name (<c>WSO2_ADMIN_USERNAME</c>).</summary>
    [Required]
    public string? AdminUserName { get; set; }

    /// <summary>Gets or sets the WSO2 super admin password (<c>WSO2_ADMIN_PASSWORD</c>).</summary>
    [Required]
    public string? AdminPassword { get; set; }

    /// <summary>Gets or sets the public base URL of the portal; redirect and logout URLs are derived from it.</summary>
    [Required]
    public Uri? PortalBaseUrl { get; set; }

    /// <summary>
    /// Gets or sets the URL WSO2 calls for back-channel logout. Defaults to <c>{PortalBaseUrl}/signout-backchannel</c>;
    /// set it when WSO2 reaches the portal through another host name (for example inside Docker).
    /// </summary>
    public Uri? BackchannelLogoutUrl { get; set; }

    /// <summary>Gets or sets the password every demo user gets (<c>DEMO_USER_PASSWORD</c>).</summary>
    [Required]
    [MinLength(MinimumDemoPasswordLength)]
    public string? DemoUserPassword { get; set; }

    /// <summary>Gets or sets a value indicating whether approvers and administrators must pass TOTP at sign-in.</summary>
    public bool EnforceMfa { get; set; } = true;

    /// <summary>
    /// Gets or sets the users who always get the TOTP step, whatever <see cref="EnforceMfa"/> says; the tool pre-enrols
    /// them. Empty by default because configuration binding appends to a list's initial items; appsettings.json lists
    /// <c>approver.mfa</c>.
    /// </summary>
    public IList<string> MfaAlwaysUsers { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether self-registration, account recovery and the My Account app are turned off,
    /// so demo users cannot change their password or MFA. Turn off only for an installation with real users.
    /// </summary>
    public bool LockDownSelfService { get; set; } = true;

    /// <summary>Gets or sets how many failed sign-ins in a row lock a WSO2 account (ADR 0033).</summary>
    [Range(3, 20)]
    public int FailedSignInsBeforeLock { get; set; } = 5;

    /// <summary>Gets or sets how long a locked WSO2 account stays locked, in minutes.</summary>
    [Range(1, 1440)]
    public int AccountLockMinutes { get; set; } = 5;

    /// <summary>Gets or sets the file the generated client secrets are written to (git-ignored).</summary>
    [Required]
    public string EnvFilePath { get; set; } = ".env.generated";

    /// <summary>Gets or sets the institution code of the public Swagger demo client.</summary>
    [Required]
    public string DemoApiInstitutionCode { get; set; } = "HLB";

    /// <summary>
    /// Gets or sets the public base URL of the API. Swagger UI asks WSO2 for tokens from the browser, so the demo
    /// client allows this origin (ADR 0027). Without it, Swagger UI cannot sign in.
    /// </summary>
    public Uri? ApiBaseUrl { get; set; }

    /// <summary>Gets the origin of <see cref="ApiBaseUrl"/> (scheme, host and port), or <see langword="null"/> if unset.</summary>
    public string? ApiOrigin => ApiBaseUrl?.GetLeftPart(UriPartial.Authority);

    /// <summary>Gets the effective back-channel logout URL.</summary>
    public Uri EffectiveBackchannelLogoutUrl =>
        BackchannelLogoutUrl ?? new Uri(PortalBase(), "signout-backchannel");

    /// <summary>Gets the OIDC redirect URL.</summary>
    public Uri RedirectUrl => new(PortalBase(), "signin-oidc");

    /// <summary>Gets the post-logout redirect URL.</summary>
    public Uri PostLogoutRedirectUrl => new(PortalBase(), "signout-callback-oidc");

    // A relative reference replaces the last path segment of a base without a trailing slash, so
    // https://example.org/portal would otherwise lose /portal from every derived URL.
    private Uri PortalBase()
    {
        var portal = PortalBaseUrl ?? throw new InvalidOperationException($"{SectionName}:{nameof(PortalBaseUrl)} is not configured.");
        return portal.AbsolutePath.EndsWith(PathSeparator) ? portal : new Uri(portal.GetLeftPart(UriPartial.Path) + PathSeparator);
    }
}
