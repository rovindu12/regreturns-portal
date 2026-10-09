namespace RegReturns.Web.Security;

/// <summary>
/// Where the portal keeps the keys that protect its session, antiforgery and temporary-data cookies (section
/// <c>DataProtection</c>, ADR 0034). A container's file system is read-only and lost on every deploy, so in production
/// the keys live on a volume of their own; without one, every restart would sign everybody out.
/// </summary>
public sealed class PortalDataProtectionOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "DataProtection";

    /// <summary>
    /// Gets or sets the folder the key ring is kept in (for example <c>/var/lib/regreturns/keys</c>). Empty means
    /// ASP.NET Core's default location for the user, which is right for <c>dotnet run</c>.
    /// </summary>
    public string? KeysPath { get; set; }
}
