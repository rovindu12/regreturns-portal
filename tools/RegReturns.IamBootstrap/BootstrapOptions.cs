using System.ComponentModel.DataAnnotations;

namespace RegReturns.IamBootstrap;

/// <summary>Settings for the IAM setup tool (section <c>IamBootstrap</c>).</summary>
internal sealed class BootstrapOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "IamBootstrap";

    /// <summary>Minimum length of the shared demo user password.</summary>
    public const int MinimumDemoPasswordLength = 12;

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

    /// <summary>Gets or sets the users who always get the TOTP step, whatever <see cref="EnforceMfa"/> says.</summary>
    public IList<string> MfaAlwaysUsers { get; set; } = ["approver.mfa"];

    /// <summary>Gets or sets the file the generated client secrets are written to (git-ignored).</summary>
    [Required]
    public string EnvFilePath { get; set; } = ".env.generated";

    /// <summary>Gets or sets the institution code of the public Swagger demo client.</summary>
    [Required]
    public string DemoApiInstitutionCode { get; set; } = "HLB";

    /// <summary>Gets the effective back-channel logout URL.</summary>
    public Uri EffectiveBackchannelLogoutUrl =>
        BackchannelLogoutUrl ?? new Uri(RequirePortal(), "signout-backchannel");

    /// <summary>Gets the OIDC redirect URL.</summary>
    public Uri RedirectUrl => new(RequirePortal(), "signin-oidc");

    /// <summary>Gets the post-logout redirect URL.</summary>
    public Uri PostLogoutRedirectUrl => new(RequirePortal(), "signout-callback-oidc");

    private Uri RequirePortal() =>
        PortalBaseUrl ?? throw new InvalidOperationException($"{SectionName}:{nameof(PortalBaseUrl)} is not configured.");
}
