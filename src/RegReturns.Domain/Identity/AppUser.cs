using RegReturns.Domain.Auditing;
using RegReturns.Domain.Common;

namespace RegReturns.Domain.Identity;

/// <summary>
/// A directory projection of a person known to WSO2 Identity Server.
/// No credentials are stored here: WSO2 is the single source of identity.
/// </summary>
public sealed class AppUser : Entity
{
    /// <summary>Maximum length of a user name.</summary>
    public const int UserNameMaxLength = 64;

    /// <summary>Maximum length of a display name.</summary>
    public const int DisplayNameMaxLength = 128;

    /// <summary>Maximum length of an e-mail address.</summary>
    public const int EmailMaxLength = 256;

    private readonly List<Role> _roles = [];

    private AppUser()
    {
        UserName = string.Empty;
        DisplayName = string.Empty;
        Email = string.Empty;
    }

    /// <summary>Gets the WSO2 user name (unique).</summary>
    public string UserName { get; private set; }

    /// <summary>Gets the display name.</summary>
    public string DisplayName { get; private set; }

    /// <summary>Gets the e-mail address. Not audited: it is personal contact data and WSO2 holds its history.</summary>
    [NotAudited]
    public string Email { get; private set; }

    /// <summary>Gets the bank the user works for, or <see langword="null"/> for regulator staff.</summary>
    public Guid? InstitutionId { get; private set; }

    /// <summary>Gets the roles assigned to the user.</summary>
    public IReadOnlyCollection<Role> Roles => _roles.AsReadOnly();

    /// <summary>Gets whether the user may sign in.</summary>
    public UserStatus Status { get; private set; }

    /// <summary>Gets the WSO2 SCIM user id (the OIDC <c>sub</c> claim) once the user is linked to WSO2.</summary>
    public string? Wso2UserId { get; private set; }

    /// <summary>Gets a value indicating whether this is a shared demo account that visitors cannot modify.</summary>
    public bool IsDemoAccount { get; private set; }

    /// <summary>
    /// Creates a user, enforcing role and institution rules:
    /// bank roles require a bank; regulator roles forbid one; bank and regulator roles cannot be mixed.
    /// </summary>
    /// <param name="userName">The WSO2 user name.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="email">The e-mail address.</param>
    /// <param name="institutionId">The user's bank, if a bank user.</param>
    /// <param name="roles">At least one role.</param>
    /// <param name="isDemoAccount">Whether the account is a protected demo account.</param>
    /// <returns>The user, or the rule that was broken.</returns>
    public static Result<AppUser> Create(
        string userName,
        string displayName,
        string email,
        Guid? institutionId,
        IEnumerable<Role> roles,
        bool isDemoAccount = false)
    {
        ArgumentNullException.ThrowIfNull(roles);
        var roleSet = roles.Distinct().Order().ToList();

        var roleCheck = CheckRoles(roleSet, institutionId);
        if (roleCheck.IsFailure)
        {
            return roleCheck.Error!;
        }

        var user = new AppUser
        {
            UserName = Guard.NotBlank(userName, UserNameMaxLength).ToLowerInvariant(),
            DisplayName = Guard.NotBlank(displayName, DisplayNameMaxLength),
            Email = Guard.NotBlank(email, EmailMaxLength).ToLowerInvariant(),
            InstitutionId = institutionId,
            Status = UserStatus.Active,
            IsDemoAccount = isDemoAccount,
        };
        user._roles.AddRange(roleSet);
        return user;
    }

    /// <summary>
    /// Brings the projection in line with WSO2 (the source of truth) after a sign-in:
    /// display name, e-mail, institution and roles, subject to the same role rules as <see cref="Create"/>.
    /// </summary>
    /// <param name="displayName">The display name from the directory.</param>
    /// <param name="email">The e-mail address from the directory.</param>
    /// <param name="institutionId">The user's bank, if a bank user.</param>
    /// <param name="roles">The roles the directory grants.</param>
    /// <returns>Failure if the directory data breaks a role rule; the projection is then left unchanged.</returns>
    public Result SyncFromDirectory(string displayName, string email, Guid? institutionId, IEnumerable<Role> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        var roleSet = roles.Distinct().Order().ToList();
        var roleCheck = CheckRoles(roleSet, institutionId);
        if (roleCheck.IsFailure)
        {
            return roleCheck;
        }

        DisplayName = Guard.NotBlank(displayName, DisplayNameMaxLength);
        Email = Guard.NotBlank(email, EmailMaxLength).ToLowerInvariant();
        InstitutionId = institutionId;
        _roles.Clear();
        _roles.AddRange(roleSet);
        return Result.Success();
    }

    /// <summary>Links the user to their WSO2 account after provisioning or first sign-in.</summary>
    /// <param name="wso2UserId">The SCIM user id.</param>
    public void LinkIdentity(string wso2UserId) => Wso2UserId = Guard.NotBlank(wso2UserId, 64);

    /// <summary>Disables the user.</summary>
    /// <returns>Failure if the user is a demo account.</returns>
    public Result Disable()
    {
        if (IsDemoAccount)
        {
            return IdentityErrors.DemoAccountProtected;
        }

        Status = UserStatus.Disabled;
        return Result.Success();
    }

    /// <summary>Re-enables a disabled user.</summary>
    public void Enable() => Status = UserStatus.Active;

    /// <summary>Creates the domain <see cref="Actor"/> for this user.</summary>
    /// <returns>An actor carrying the user's id, name, roles and bank.</returns>
    public Actor ToActor() => new(Id, DisplayName, _roles, InstitutionId);

    private static Result CheckRoles(List<Role> roles, Guid? institutionId)
    {
        if (roles.Count == 0)
        {
            return IdentityErrors.RoleRequired;
        }

        var bankRoles = roles.Count(r => r.IsBankRole());
        if (bankRoles > 0 && bankRoles < roles.Count)
        {
            return IdentityErrors.MixedRoles;
        }

        if (bankRoles > 0 && institutionId is null)
        {
            return IdentityErrors.InstitutionRequired;
        }

        if (bankRoles == 0 && institutionId is not null)
        {
            return IdentityErrors.InstitutionNotAllowed;
        }

        return Result.Success();
    }
}
