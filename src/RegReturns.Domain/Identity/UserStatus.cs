namespace RegReturns.Domain.Identity;

/// <summary>Whether a user may sign in.</summary>
public enum UserStatus
{
    /// <summary>The user may sign in.</summary>
    Active = 1,

    /// <summary>The user is disabled here and in WSO2.</summary>
    Disabled = 2,
}
