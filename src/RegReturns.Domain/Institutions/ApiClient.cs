using RegReturns.Domain.Common;

namespace RegReturns.Domain.Institutions;

/// <summary>
/// A bank's machine-to-machine client registered in WSO2 Identity Server.
/// Maps the <c>azp</c>/<c>client_id</c> of a client-credentials token to the one institution it may act for.
/// </summary>
public sealed class ApiClient : Entity
{
    /// <summary>Maximum length of a WSO2 client id.</summary>
    public const int ClientIdMaxLength = 128;

    /// <summary>Maximum length of a client display name.</summary>
    public const int NameMaxLength = 128;

    private ApiClient()
    {
        Wso2ClientId = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Gets the institution the client acts for.</summary>
    public Guid InstitutionId { get; private set; }

    /// <summary>Gets the OAuth client id issued by WSO2 (unique).</summary>
    public string Wso2ClientId { get; private set; }

    /// <summary>Gets the application name in WSO2.</summary>
    public string Name { get; private set; }

    /// <summary>Gets a value indicating whether tokens issued to this client are accepted.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Registers an active client for an institution.</summary>
    /// <param name="institution">The institution the client acts for.</param>
    /// <param name="wso2ClientId">The OAuth client id issued by WSO2.</param>
    /// <param name="name">The application name in WSO2.</param>
    /// <returns>The new client.</returns>
    public static ApiClient Create(Institution institution, string wso2ClientId, string name)
    {
        ArgumentNullException.ThrowIfNull(institution);
        return new ApiClient
        {
            InstitutionId = institution.Id,
            Wso2ClientId = Guard.NotBlank(wso2ClientId, ClientIdMaxLength),
            Name = Guard.NotBlank(name, NameMaxLength),
            IsActive = true,
        };
    }

    /// <summary>Points the record at a re-created WSO2 application, keeping the institution.</summary>
    /// <param name="wso2ClientId">The new OAuth client id.</param>
    /// <param name="name">The application name in WSO2.</param>
    public void Relink(string wso2ClientId, string name)
    {
        Wso2ClientId = Guard.NotBlank(wso2ClientId, ClientIdMaxLength);
        Name = Guard.NotBlank(name, NameMaxLength);
        IsActive = true;
    }

    /// <summary>Stops accepting tokens issued to this client.</summary>
    public void Deactivate() => IsActive = false;
}
