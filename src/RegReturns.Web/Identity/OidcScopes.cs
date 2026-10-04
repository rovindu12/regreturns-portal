namespace RegReturns.Web.Identity;

/// <summary>The scopes the portal requests from WSO2 at sign-in.</summary>
public static class OidcScopes
{
    /// <summary>Required for OpenID Connect; yields <c>sub</c> and the id_token.</summary>
    public const string OpenId = "openid";

    /// <summary>Name claims (<c>name</c>, <c>given_name</c>, <c>family_name</c>, <c>username</c>).</summary>
    public const string Profile = "profile";

    /// <summary>The <c>email</c> claim, required to link the user directory projection.</summary>
    public const string Email = "email";

    /// <summary>The <c>roles</c> claim with the user's application roles.</summary>
    public const string Roles = "roles";

    /// <summary>Custom WSO2 scope carrying the <c>institution_id</c> claim for bank users.</summary>
    public const string Institution = "institution";

    /// <summary>Gets every scope requested at sign-in.</summary>
    public static IReadOnlyList<string> All { get; } = [OpenId, Profile, Email, Roles, Institution];
}
