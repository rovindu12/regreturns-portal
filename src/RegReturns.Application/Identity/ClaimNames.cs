namespace RegReturns.Application.Identity;

/// <summary>
/// Names of the claims RegReturns reads from WSO2 tokens. Inbound claim mapping is switched off,
/// so these are the JWT claim names exactly as WSO2 issues them.
/// </summary>
public static class ClaimNames
{
    /// <summary>The WSO2 user id (SCIM id), stable for the life of the account.</summary>
    public const string Subject = "sub";

    /// <summary>The WSO2 user name.</summary>
    public const string UserName = "username";

    /// <summary>The display name.</summary>
    public const string Name = "name";

    /// <summary>The e-mail address.</summary>
    public const string Email = "email";

    /// <summary>Application roles (only known <see cref="RoleNames"/> survive claim mapping).</summary>
    public const string Roles = "roles";

    /// <summary>The bank's <c>Institution.Code</c>; absent for regulator staff.</summary>
    public const string InstitutionId = "institution_id";

    /// <summary>Authentication methods used at sign-in, used to prove TOTP was performed.</summary>
    public const string AuthenticationMethods = "amr";

    /// <summary>The WSO2 session id, used to end portal sessions on back-channel logout.</summary>
    public const string SessionId = "sid";

    /// <summary>The client the token was issued to (client-credentials tokens).</summary>
    public const string AuthorizedParty = "azp";

    /// <summary>The OAuth client id (client-credentials tokens).</summary>
    public const string ClientId = "client_id";

    /// <summary>Space-separated OAuth scopes granted to the token.</summary>
    public const string Scope = "scope";

    /// <summary>
    /// WSO2's token subject type: <see cref="ApplicationTokenType"/> for client-credentials tokens,
    /// <c>APPLICATION_USER</c> for tokens issued to a signed-in user.
    /// </summary>
    public const string AuthorizedUserType = "aut";

    /// <summary>The <see cref="AuthorizedUserType"/> value of a client-credentials (machine-to-machine) token.</summary>
    public const string ApplicationTokenType = "APPLICATION";
}
