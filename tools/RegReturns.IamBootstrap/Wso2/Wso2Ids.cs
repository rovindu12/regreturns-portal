using System.Diagnostics.CodeAnalysis;
using System.Text;

using RegReturns.Application.Identity;

namespace RegReturns.IamBootstrap.Wso2;

/// <summary>Identifier helpers for WSO2's claim management API.</summary>
[SuppressMessage("Security", "S5332:Using http protocol is insecure", Justification = "WSO2 claim dialect URIs are identifiers, not URLs that are fetched.")]
[SuppressMessage("Major Code Smell", "S1075:URIs should not be hardcoded", Justification = "Fixed WSO2 dialect identifiers.")]
internal static class Wso2Ids
{
    /// <summary>The id of the local claim dialect (<c>http://wso2.org/claims</c>).</summary>
    public const string LocalDialect = "local";

    /// <summary>The OpenID Connect claim dialect URI.</summary>
    public const string OidcDialectUri = "http://wso2.org/oidc/claim";

    /// <summary>Local claim: user name.</summary>
    public const string UserNameClaim = "http://wso2.org/claims/username";

    /// <summary>Local claim: e-mail address.</summary>
    public const string EmailClaim = "http://wso2.org/claims/emailaddress";

    /// <summary>Local claim: full name, released as OIDC <c>name</c>.</summary>
    public const string FullNameClaim = "http://wso2.org/claims/fullname";

    /// <summary>Local claim: roles (computed from role assignments).</summary>
    public const string RolesClaim = "http://wso2.org/claims/roles";

    /// <summary>Local claim: the immutable user id, used as <c>sub</c>.</summary>
    public const string UserIdClaim = "http://wso2.org/claims/userid";

    /// <summary>
    /// The identity claims in which WSO2's TOTP authenticator keeps a user's secret (encrypted) and the secret being
    /// enrolled. Emptying them makes the TOTP step treat the user as not enrolled.
    /// </summary>
    public static readonly IReadOnlyList<string> TotpSecretClaims =
    [
        "http://wso2.org/claims/identity/secretkey",
        "http://wso2.org/claims/identity/verifySecretkey",
    ];

    /// <summary>The SCIM 2 custom user schema, the only SCIM schema 7.3 lets us extend.</summary>
    public const string ScimCustomUserSchema = TotpEnrolmentClaim.ScimSchema;

    /// <summary>
    /// Returns the id WSO2 uses for a claim dialect or claim URI: base64url of the UTF-8 URI without padding.
    /// </summary>
    /// <param name="uri">The dialect or claim URI.</param>
    /// <returns>The id.</returns>
    public static string ForUri(string uri) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(uri)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Escapes a value for a SCIM or management API <c>filter</c> expression in a query string.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The escaped value.</returns>
    public static string Filter(string value) => Uri.EscapeDataString(value);
}
