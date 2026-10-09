using RegReturns.Domain.Common;

namespace RegReturns.Domain.Identity;

/// <summary>Business errors raised by identity rules.</summary>
public static class IdentityErrors
{
    /// <summary>A user must hold at least one role.</summary>
    public static readonly Error RoleRequired = new("User.RoleRequired", "A user must have at least one role.");

    /// <summary>Bank roles and regulator roles cannot be combined.</summary>
    public static readonly Error MixedRoles = new(
        "User.MixedRoles", "Bank roles and regulator roles cannot be assigned to the same user.");

    /// <summary>Bank users must belong to a bank.</summary>
    public static readonly Error InstitutionRequired = new(
        "User.InstitutionRequired", "Bank users must belong to an institution.");

    /// <summary>Regulator staff cannot belong to a bank.</summary>
    public static readonly Error InstitutionNotAllowed = new(
        "User.InstitutionNotAllowed", "Regulator staff cannot belong to an institution.");

    /// <summary>Demo accounts cannot be modified.</summary>
    public static readonly Error DemoAccountProtected = new(
        "User.DemoAccountProtected", "Demo accounts cannot be changed.");

    /// <summary>The user name is reserved for an API client user or a system account.</summary>
    public static readonly Error ReservedUserName = new(
        "User.ReservedUserName", "The user name is reserved for an API client or a system account.");
}
