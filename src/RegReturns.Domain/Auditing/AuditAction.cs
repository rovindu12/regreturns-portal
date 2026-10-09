namespace RegReturns.Domain.Auditing;

/// <summary>What an audit entry records.</summary>
public enum AuditAction
{
    /// <summary>A user signed in to the portal.</summary>
    SignIn = 1,

    /// <summary>A user signed out of the portal, or their session was ended by WSO2.</summary>
    SignOut = 2,

    /// <summary>An authenticated caller was refused access by an authorization policy.</summary>
    AccessDenied = 3,

    /// <summary>An API caller presented a token that was rejected.</summary>
    AuthenticationFailed = 4,

    /// <summary>An aggregate was created (with its child rows); the entry carries the values it was created with.</summary>
    Created = 5,

    /// <summary>An aggregate or its child rows changed; the entry carries the before and after values.</summary>
    Updated = 6,

    /// <summary>An aggregate was deleted; the entry carries the values it had.</summary>
    Deleted = 7,

    /// <summary>An aggregate's <c>Status</c> changed (a workflow or lifecycle step), possibly with other values.</summary>
    StateChanged = 8,

    /// <summary>Someone verified the hash chain; the details carry the outcome.</summary>
    ChainVerified = 9,

    /// <summary>Someone exported a report; the details say which report, scope and format, never its figures.</summary>
    ReportExported = 10,

    /// <summary>
    /// A supervisor had an advisory insight generated for a return (ADR 0030); the details name the provider and model
    /// attempted, the outcome, and the SHA-256 of the payload and content, never a figure.
    /// </summary>
    InsightGenerated = 11,

    /// <summary>
    /// The demo data was reset (ADR 0031): the workload replaced by the seed, the directory and this chain kept. The
    /// details name the trigger, the rows removed and seeded, and the duration.
    /// </summary>
    DemoReset = 12,

    /// <summary>
    /// An administrator let a person set up a new authenticator at their next sign-in (ADR 0032). The entity is the
    /// WSO2 account; the details name the user name and when the window closes.
    /// </summary>
    TotpEnrolmentOpened = 13,
}
