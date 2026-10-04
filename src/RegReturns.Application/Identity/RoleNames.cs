using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

using RegReturns.Domain.Identity;

namespace RegReturns.Application.Identity;

/// <summary>
/// The application role names defined in WSO2 Identity Server and their mapping to <see cref="Role"/>.
/// Role claim values outside this map are dropped at sign-in.
/// </summary>
public static class RoleNames
{
    /// <summary>WSO2 role for <see cref="Role.BankMaker"/>.</summary>
    public const string BankMaker = "bank_maker";

    /// <summary>WSO2 role for <see cref="Role.BankChecker"/>.</summary>
    public const string BankChecker = "bank_checker";

    /// <summary>WSO2 role for <see cref="Role.SupervisorReviewer"/>.</summary>
    public const string SupervisorReviewer = "supervisor_reviewer";

    /// <summary>WSO2 role for <see cref="Role.SupervisorApprover"/>.</summary>
    public const string SupervisorApprover = "supervisor_approver";

    /// <summary>WSO2 role for <see cref="Role.SystemAdmin"/>.</summary>
    public const string SystemAdmin = "system_admin";

    /// <summary>WSO2 role for <see cref="Role.Auditor"/>.</summary>
    public const string Auditor = "auditor";

    private static readonly FrozenDictionary<string, Role> ToRole = new Dictionary<string, Role>(StringComparer.Ordinal)
    {
        [BankMaker] = Role.BankMaker,
        [BankChecker] = Role.BankChecker,
        [SupervisorReviewer] = Role.SupervisorReviewer,
        [SupervisorApprover] = Role.SupervisorApprover,
        [SystemAdmin] = Role.SystemAdmin,
        [Auditor] = Role.Auditor,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<Role, string> ToName =
        ToRole.ToFrozenDictionary(pair => pair.Value, pair => pair.Key);

    /// <summary>Gets every WSO2 role name, in <see cref="Role"/> order.</summary>
    public static IReadOnlyList<string> All { get; } = [.. ToRole.OrderBy(pair => pair.Value).Select(pair => pair.Key)];

    /// <summary>Maps a WSO2 role name to a <see cref="Role"/>.</summary>
    /// <param name="name">The role claim value (case-sensitive).</param>
    /// <param name="role">The matching role.</param>
    /// <returns><see langword="true"/> if the name is a known role.</returns>
    public static bool TryParse(string? name, [NotNullWhen(true)] out Role? role)
    {
        if (name is not null && ToRole.TryGetValue(name, out var found))
        {
            role = found;
            return true;
        }

        role = null;
        return false;
    }

    /// <summary>Returns the WSO2 role name for a <see cref="Role"/>.</summary>
    /// <param name="role">The role.</param>
    /// <returns>The WSO2 role name.</returns>
    public static string For(Role role) => ToName[role];
}
