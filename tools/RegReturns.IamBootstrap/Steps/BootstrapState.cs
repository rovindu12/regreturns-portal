namespace RegReturns.IamBootstrap.Steps;

/// <summary>Ids and secrets discovered by earlier steps and needed by later ones.</summary>
internal sealed class BootstrapState
{
    /// <summary>Gets or sets a value indicating whether demo users get their password (and TOTP) reset even if they exist.</summary>
    public bool ResetDemoUsers { get; set; }

    /// <summary>Gets or sets the id of the RegReturns API resource.</summary>
    public string? ApiResourceId { get; set; }

    /// <summary>Gets or sets the WSO2 application id of the portal.</summary>
    public string? PortalAppId { get; set; }

    /// <summary>Gets the WSO2 role ids by role name.</summary>
    public Dictionary<string, string> RoleIds { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the WSO2 user ids by user name.</summary>
    public Dictionary<string, string> UserIds { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the values written to the generated env file (secrets: never logged).</summary>
    public SortedDictionary<string, string> GeneratedSettings { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the per-object outcomes, for the summary.</summary>
    public List<(string Kind, string Name, Outcome Outcome)> Changes { get; } = [];

    /// <summary>Records an outcome.</summary>
    /// <param name="kind">The kind of object.</param>
    /// <param name="name">Its name.</param>
    /// <param name="outcome">What happened.</param>
    public void Record(string kind, string name, Outcome outcome) => Changes.Add((kind, name, outcome));
}
