using RegReturns.Application.Authorization;

namespace RegReturns.Web.Navigation;

/// <summary>A top-level area of the portal and the policy that opens it.</summary>
/// <param name="Title">The area's name in the navigation and on its landing page.</param>
/// <param name="Route">The route of its landing page, without a leading slash.</param>
/// <param name="Policy">The policy a user needs to open it.</param>
/// <param name="Summary">One sentence about what the area is for.</param>
public sealed record PortalArea(string Title, string Route, string Policy, string Summary)
{
    /// <summary>Gets the local URL of the landing page.</summary>
    public string Path => "/" + Route;
}

/// <summary>The portal's areas, in navigation order. Controllers route and authorize with the same constants.</summary>
public static class PortalAreas
{
    /// <summary>Route of the bank area.</summary>
    public const string BankRoute = "bank";

    /// <summary>Route of the supervision area.</summary>
    public const string SupervisionRoute = "supervision";

    /// <summary>Route of the reports area.</summary>
    public const string ReportsRoute = "reports";

    /// <summary>Route of the audit area.</summary>
    public const string AuditRoute = "audit";

    /// <summary>Route of the administration area.</summary>
    public const string AdminRoute = "admin";

    /// <summary>Gets the bank area: makers and checkers of a licensed bank.</summary>
    public static PortalArea Bank { get; } = new(
        "Bank returns", BankRoute, Policies.BankAccess, "Prepare, check and submit your bank's regulatory returns.");

    /// <summary>Gets the supervision area: reviewers and approvers at the Bank of Valoria.</summary>
    public static PortalArea Supervision { get; } = new(
        "Supervision", SupervisionRoute, Policies.SupervisionAccess, "Review, approve or return the returns banks have submitted.");

    /// <summary>Gets the reports area: every role, scoped to the user's institution for bank users.</summary>
    public static PortalArea Reports { get; } = new(
        "Reports", ReportsRoute, Policies.ReportsView, "Dashboards of filing status, late returns and key figures.");

    /// <summary>Gets the audit area: auditors and administrators.</summary>
    public static PortalArea Audit { get; } = new(
        "Audit trail", AuditRoute, Policies.AuditRead, "Read the tamper-evident audit trail and verify its hash chain.");

    /// <summary>Gets the administration area: administrators who signed in with two-step verification.</summary>
    public static PortalArea Admin { get; } = new(
        "Administration", AdminRoute, Policies.AdminManage, "Manage institutions, users and return templates.");

    /// <summary>Gets every area, in navigation order.</summary>
    public static IReadOnlyList<PortalArea> All { get; } = [Bank, Supervision, Reports, Audit, Admin];
}
