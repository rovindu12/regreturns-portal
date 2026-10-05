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
}
