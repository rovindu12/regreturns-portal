using System.ComponentModel.DataAnnotations;

namespace RegReturns.Application.Demo;

/// <summary>
/// Settings of the public demo (configuration section <c>Demo</c>, ADR 0031). Off by default: only a demo deployment
/// shows the demo pages and the published credentials, and only it can be reset.
/// </summary>
public sealed class DemoOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Demo";

    /// <summary>Gets or sets a value indicating whether this deployment is the public demo.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the password every demo account shares (<c>DEMO_USER_PASSWORD</c>), published on the demo page.
    /// Secret: set it in user-secrets or the environment, never in a settings file.
    /// </summary>
    public string? UserPassword { get; set; }

    /// <summary>
    /// Gets the TOTP secrets of the demo accounts that sign in with a second factor, keyed like IamBootstrap's
    /// <c>TOTP_SECRET_&lt;USER&gt;</c> settings without the prefix (<see cref="DemoAccounts.SettingSuffix"/>), for
    /// example <c>Demo:TotpSecrets:APPROVER_MFA</c>. Published on the demo page.
    /// </summary>
    public Dictionary<string, string> TotpSecrets { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the client id of the public, read-only Swagger demo client (<c>DEMO_API_CLIENT_ID</c>).</summary>
    [MaxLength(128)]
    public string? ApiClientId { get; set; }

    /// <summary>Gets or sets that client's secret (<c>DEMO_API_CLIENT_SECRET</c>), published on the demo page.</summary>
    public string? ApiClientSecret { get; set; }

    /// <summary>Gets or sets the public base address of the API, for the links to Swagger UI.</summary>
    public Uri? ApiBaseUrl { get; set; }

    /// <summary>
    /// Gets or sets when the demo resets itself: a five-field cron expression in UTC (default <c>0 3 * * *</c> in
    /// appsettings.json). Empty switches the scheduled reset off.
    /// </summary>
    public string? ResetSchedule { get; set; }

    /// <summary>Gets or sets how long after any reset the administrator's button is refused.</summary>
    [Range(1, 1440)]
    public int ResetCooldownMinutes { get; set; } = 10;

    /// <summary>Gets the configured TOTP secret of a demo account, if it has one.</summary>
    /// <param name="userName">The account's user name.</param>
    /// <returns>The Base32 secret, or <see langword="null"/>.</returns>
    public string? TotpSecretFor(string userName)
    {
        var suffix = DemoAccounts.SettingSuffix(userName);
        var secret = TotpSecrets.FirstOrDefault(kv => string.Equals(kv.Key, suffix, StringComparison.OrdinalIgnoreCase)).Value;
        return string.IsNullOrWhiteSpace(secret) ? null : secret.Trim();
    }
}
