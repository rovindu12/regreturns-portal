namespace RegReturns.Application.Identity;

/// <summary>OAuth scopes of the RegReturns API resource in WSO2.</summary>
public static class ApiScopes
{
    /// <summary>The API resource identifier, carried in the <c>aud</c> claim of API tokens.</summary>
    public const string ApiIdentifier = "https://api.regreturns";

    /// <summary>Read the calling bank's submissions.</summary>
    public const string ReturnsRead = "returns:read";

    /// <summary>Deliver returns for the calling bank, as drafts a bank checker submits in the portal (ADR 0026).</summary>
    public const string ReturnsSubmit = "returns:submit";

    /// <summary>Read reference data (institutions, return types).</summary>
    public const string ReferenceRead = "reference:read";

    /// <summary>Gets every scope of the API resource.</summary>
    public static IReadOnlyList<string> All { get; } = [ReturnsRead, ReturnsSubmit, ReferenceRead];
}
