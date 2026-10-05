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

    /// <summary>The <c>amr</c> values that prove a TOTP step when none are configured.</summary>
    public static readonly IReadOnlyList<string> DefaultMfaAuthenticationMethods = ["totp"];

    /// <summary>
    /// Gets or sets the <c>amr</c> values that prove a TOTP step happened (compared case-insensitively). Empty means
    /// <see cref="DefaultMfaAuthenticationMethods"/>. Empty by default because configuration binding appends to a
    /// list's initial items, so a configured list could otherwise never drop <c>totp</c>.
    /// </summary>
    public IList<string> MfaAuthenticationMethods { get; set; } = [];

    /// <summary>Returns the <c>amr</c> values in effect: the configured ones, or the defaults when none are configured.</summary>
    /// <returns>The accepted methods.</returns>
    public IReadOnlyCollection<string> AcceptedMfaAuthenticationMethods() =>
        MfaAuthenticationMethods.Count == 0 ? DefaultMfaAuthenticationMethods : [.. MfaAuthenticationMethods];
}
