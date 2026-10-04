namespace RegReturns.Infrastructure.Identity.Authorization;

/// <summary>Identity and access settings shared by the portal and the API (section <c>Iam</c>).</summary>
public sealed class IamOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Iam";

    /// <summary>
    /// Gets or sets a value indicating whether approvers and administrators must have signed in with TOTP.
    /// WSO2's adaptive script asks for TOTP; the app checks <c>amr</c> again so a drifted WSO2 config cannot bypass it.
    /// </summary>
    public bool EnforceMfa { get; set; } = true;

    /// <summary>
    /// Gets or sets the <c>amr</c> values that prove a TOTP step happened (compared case-insensitively).
    /// </summary>
    public IList<string> MfaAuthenticationMethods { get; set; } = ["totp"];
}
