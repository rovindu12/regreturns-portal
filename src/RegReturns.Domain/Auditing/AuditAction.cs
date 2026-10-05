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
}
