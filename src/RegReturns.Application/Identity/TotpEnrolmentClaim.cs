using System.Diagnostics.CodeAnalysis;

namespace RegReturns.Application.Identity;

/// <summary>
/// The WSO2 user attribute that opens a TOTP enrolment window for one user (ADR 0032). It holds the end of the window
/// in Unix milliseconds; while it lies in the future, the portal's sign-in script replaces the user's authenticator at
/// their next sign-in, and closes the window once they have proved the new one. IamBootstrap creates the claim, the
/// portal sets it over SCIM.
/// </summary>
[SuppressMessage("Security", "S5332:Using http protocol is insecure", Justification = "WSO2 claim URIs are identifiers, not URLs that are fetched.")]
public static class TotpEnrolmentClaim
{
    /// <summary>The WSO2 local claim.</summary>
    public const string LocalClaim = "http://wso2.org/claims/totp_enrolment_until";

    /// <summary>The user store attribute behind <see cref="LocalClaim"/>.</summary>
    public const string StoreAttribute = "totpEnrolmentUntil";

    /// <summary>The SCIM 2 extension schema that carries RegReturns' own user attributes.</summary>
    public const string ScimSchema = "urn:scim:schemas:extension:custom:User";

    /// <summary>The attribute in <see cref="ScimSchema"/> mapped to <see cref="LocalClaim"/>.</summary>
    public const string ScimAttribute = "totpEnrolmentUntil";

    /// <summary>The value that means no window is open (the script writes it once the user has enrolled).</summary>
    public const string Closed = "0";
}
