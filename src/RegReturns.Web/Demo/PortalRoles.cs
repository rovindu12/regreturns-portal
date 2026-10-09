using RegReturns.Application.Identity;
using RegReturns.Domain.Identity;
using RegReturns.Web.Navigation;

namespace RegReturns.Web.Demo;

/// <summary>What a role is for, as the landing and demo pages explain it.</summary>
/// <param name="Role">The role.</param>
/// <param name="Title">The role's name for people.</param>
/// <param name="Wso2Role">The WSO2 role that grants it.</param>
/// <param name="Area">Where the role starts in the portal.</param>
/// <param name="CanDo">What the role can do.</param>
/// <param name="MustNot">The separation-of-duties rule or limit that applies, if any.</param>
public sealed record PortalRole(Role Role, string Title, string Wso2Role, PortalArea Area, string CanDo, string? MustNot);

/// <summary>The roles of the portal, in workflow order.</summary>
public static class PortalRoles
{
    /// <summary>Gets every role.</summary>
    public static IReadOnlyList<PortalRole> All { get; } =
    [
        new(Role.BankMaker, "Bank maker", RoleNames.BankMaker, PortalAreas.Bank,
            "Prepares the bank's returns: enters figures or uploads Excel or CSV, validates and saves drafts.",
            "Cannot submit."),
        new(Role.BankChecker, "Bank checker", RoleNames.BankChecker, PortalAreas.Bank,
            "Checks a draft, justifies every warning and submits the return to the Bank of Valoria.",
            "Cannot submit a return they prepared or last edited."),
        new(Role.SupervisorReviewer, "Supervision reviewer", RoleNames.SupervisorReviewer, PortalAreas.Supervision,
            "Picks up submitted returns, asks for an advisory insight and sends a return back for correction.",
            "Sees a return only once it is submitted."),
        new(Role.SupervisorApprover, "Supervision approver", RoleNames.SupervisorApprover, PortalAreas.Supervision,
            "Approves or rejects a return under review, after a second sign-in factor (TOTP).",
            "Cannot decide on a return they reviewed."),
        new(Role.SystemAdmin, "System administrator", RoleNames.SystemAdmin, PortalAreas.Admin,
            "Maintains return templates and their validation rules, and portal access; resets the demo.",
            "Cannot change demo accounts or their own access."),
        new(Role.Auditor, "Auditor", RoleNames.Auditor, PortalAreas.Audit,
            "Reads the audit trail and verifies its hash chain.",
            "Changes nothing."),
    ];

    /// <summary>Returns the description of a role.</summary>
    /// <param name="role">The role.</param>
    /// <returns>Its description.</returns>
    public static PortalRole Of(Role role) => All.Single(r => r.Role == role);
}
