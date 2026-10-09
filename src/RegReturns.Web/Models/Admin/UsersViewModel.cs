using RegReturns.Application.Identity;
using RegReturns.Domain.Identity;
using RegReturns.Web.Demo;

namespace RegReturns.Web.Models.Admin;

/// <summary>Data for the administrator's directory page.</summary>
/// <param name="Directory">People and API clients.</param>
public sealed record UsersViewModel(UserDirectory Directory)
{
    /// <summary>Returns the roles of a person for display.</summary>
    /// <param name="roles">The roles.</param>
    /// <returns>For example <c>Bank maker</c>.</returns>
    public static string RoleList(IEnumerable<Role> roles) =>
        string.Join(", ", roles.Select(r => PortalRoles.Of(r).Title));
}
