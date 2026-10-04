using System.Diagnostics.CodeAnalysis;

using RegReturns.Application.Identity;

namespace RegReturns.IamBootstrap;

/// <summary>Names of the WSO2 objects RegReturns owns (plan §4.2). Lookups are by these names, so renaming one creates a new object.</summary>
[SuppressMessage("Security", "S5332:Using http protocol is insecure", Justification = "WSO2 claim URIs are identifiers, not URLs that are fetched.")]
internal static class IamNames
{
    /// <summary>The local claim holding a bank user's institution code.</summary>
    public const string InstitutionLocalClaim = "http://wso2.org/claims/institution_id";

    /// <summary>The user store attribute behind <see cref="InstitutionLocalClaim"/>.</summary>
    public const string InstitutionStoreAttribute = "institutionId";

    /// <summary>The SCIM attribute (in the custom user schema) mapped to <see cref="InstitutionLocalClaim"/>.</summary>
    public const string InstitutionScimAttribute = "institutionId";

    /// <summary>The SCIM attribute (in the custom user schema) mapped to WSO2's full-name claim (OIDC <c>name</c>).</summary>
    public const string FullNameScimAttribute = "fullName";

    /// <summary>The OIDC claim released in tokens.</summary>
    public const string InstitutionOidcClaim = ClaimNames.InstitutionId;

    /// <summary>The custom OIDC scope that releases <see cref="InstitutionOidcClaim"/>.</summary>
    public const string InstitutionScope = "institution";

    /// <summary>Name of the API resource (its identifier is <see cref="ApiScopes.ApiIdentifier"/>).</summary>
    public const string ApiResourceName = "RegReturns API";

    /// <summary>Name of the portal application.</summary>
    public const string PortalApp = "RegReturns Portal";

    /// <summary>Client id of the portal application.</summary>
    public const string PortalClientId = "regreturns-portal";

    /// <summary>Name of the provisioning application.</summary>
    public const string ProvisionerApp = "RegReturns Provisioner";

    /// <summary>Client id of the provisioning application.</summary>
    public const string ProvisionerClientId = "regreturns-provisioner";

    /// <summary>Name of the public Swagger demo client.</summary>
    public const string DemoApiApp = "RegReturns Demo Bank API";

    /// <summary>Client id of the public Swagger demo client.</summary>
    public const string DemoApiClientId = "regreturns-demo-api";

    /// <summary>Access token lifetime for every RegReturns client (plan §4.2).</summary>
    public const int AccessTokenSeconds = 300;

    /// <summary>Returns the application name of a bank's machine-to-machine client.</summary>
    /// <param name="institutionCode">The bank's code.</param>
    /// <returns>The application name.</returns>
    public static string BankApp(string institutionCode) => $"RegReturns Bank {institutionCode.ToUpperInvariant()}";

    /// <summary>Returns the client id of a bank's machine-to-machine client.</summary>
    /// <param name="institutionCode">The bank's code.</param>
    /// <returns>The client id.</returns>
    public static string BankClientId(string institutionCode) => $"regreturns-bank-{institutionCode.ToLowerInvariant()}";
}
