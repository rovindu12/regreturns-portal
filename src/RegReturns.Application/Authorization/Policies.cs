namespace RegReturns.Application.Authorization;

/// <summary>
/// Authorization policy names used by the portal and the API. Every controller action names one of these
/// (or opts out with <c>AllowAnonymous</c>); the requirements are registered in Infrastructure.
/// </summary>
public static class Policies
{
    /// <summary>Any bank user (maker or checker) of an institution.</summary>
    public const string BankAccess = "Bank.Access";

    /// <summary>Prepare and edit returns: bank maker with an institution.</summary>
    public const string BankPrepareReturn = "Bank.PrepareReturn";

    /// <summary>Submit returns: bank checker with an institution (the domain also rejects the preparer).</summary>
    public const string BankSubmitReturn = "Bank.SubmitReturn";

    /// <summary>Any supervisor (reviewer or approver).</summary>
    public const string SupervisionAccess = "Supervision.Access";

    /// <summary>Pick up and review submitted returns.</summary>
    public const string SupervisionReview = "Supervision.Review";

    /// <summary>Approve or reject reviewed returns; needs TOTP when MFA is enforced.</summary>
    public const string SupervisionApprove = "Supervision.Approve";

    /// <summary>Manage institutions, users and templates; needs TOTP when MFA is enforced.</summary>
    public const string AdminManage = "Admin.Manage";

    /// <summary>Read the audit trail and verify the chain: auditor or system admin.</summary>
    public const string AuditRead = "Audit.Read";

    /// <summary>View dashboards and reports (bank users see only their own institution).</summary>
    public const string ReportsView = "Reports.View";

    /// <summary>API: read the calling bank's returns (scope <c>returns:read</c>).</summary>
    public const string ApiReturnsRead = "Api.Returns.Read";

    /// <summary>API: deliver returns for the calling bank, as drafts (scope <c>returns:submit</c>, ADR 0026).</summary>
    public const string ApiReturnsSubmit = "Api.Returns.Submit";

    /// <summary>API: read reference data (scope <c>reference:read</c>).</summary>
    public const string ApiReferenceRead = "Api.Reference.Read";
}
