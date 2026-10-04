using System.Security.Claims;

using RegReturns.Application.Identity;
using RegReturns.Domain.Identity;

namespace RegReturns.Web.Models;

/// <summary>Who is signed in, for display.</summary>
/// <param name="Name">The display name.</param>
/// <param name="InstitutionCode">The bank's institution code, or <see langword="null"/> for regulator staff.</param>
/// <param name="Roles">The user's roles as readable names.</param>
public sealed record SignedInUserViewModel(string Name, string? InstitutionCode, IReadOnlyList<string> Roles)
{
    /// <summary>Name shown when the token carried no name at all.</summary>
    public const string FallbackName = "Signed-in user";

    /// <summary>Reads the display details from the current principal.</summary>
    /// <param name="principal">The signed-in user.</param>
    /// <returns>The view model.</returns>
    public static SignedInUserViewModel From(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        var name = NullIfBlank(principal.FindFirst(ClaimNames.Name)?.Value)
            ?? NullIfBlank(principal.FindFirst(ClaimNames.UserName)?.Value)
            ?? FallbackName;
        var roles = principal.FindAll(ClaimNames.Roles)
            .Select(claim => RoleNames.TryParse(claim.Value, out var role) ? role : null)
            .OfType<Role>()
            .Distinct()
            .Order()
            .Select(Describe)
            .ToList();
        return new SignedInUserViewModel(name, NullIfBlank(principal.FindFirst(ClaimNames.InstitutionId)?.Value), roles);
    }

    /// <summary>Returns the readable name of a role.</summary>
    /// <param name="role">The role.</param>
    /// <returns>The readable name.</returns>
    public static string Describe(Role role) => role switch
    {
        Role.BankMaker => "Bank maker",
        Role.BankChecker => "Bank checker",
        Role.SupervisorReviewer => "Supervisor (reviewer)",
        Role.SupervisorApprover => "Supervisor (approver)",
        Role.SystemAdmin => "Portal administrator",
        Role.Auditor => "Auditor",
        _ => role.ToString(),
    };

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
