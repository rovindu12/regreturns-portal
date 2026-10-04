namespace RegReturns.Domain.Identity;

/// <summary>
/// Application roles. Each role is defined in WSO2 Identity Server and arrives in the <c>roles</c> claim.
/// </summary>
public enum Role
{
    /// <summary>Prepares returns for a bank.</summary>
    BankMaker = 1,

    /// <summary>Checks and submits a bank's returns.</summary>
    BankChecker = 2,

    /// <summary>Reviews submitted returns at the regulator.</summary>
    SupervisorReviewer = 3,

    /// <summary>Approves or rejects reviewed returns at the regulator.</summary>
    SupervisorApprover = 4,

    /// <summary>Manages institutions, users and return templates.</summary>
    SystemAdmin = 5,

    /// <summary>Read-only access to everything, including the audit trail.</summary>
    Auditor = 6,
}

/// <summary>Classification helpers for <see cref="Role"/>.</summary>
public static class RoleExtensions
{
    /// <summary>Returns <see langword="true"/> for roles held by staff of a supervised bank.</summary>
    /// <param name="role">The role.</param>
    /// <returns>Whether the role belongs to a bank user.</returns>
    public static bool IsBankRole(this Role role) => role is Role.BankMaker or Role.BankChecker;
}
