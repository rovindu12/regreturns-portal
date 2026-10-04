using RegReturns.Domain.Common;

namespace RegReturns.Web.Identity;

/// <summary>Reasons the portal refuses an otherwise valid WSO2 sign-in, before the user is linked.</summary>
public static class SignInErrors
{
    /// <summary>The id_token has no <c>sub</c> claim.</summary>
    public static readonly Error SubjectMissing = new("User.SubjectMissing", "The sign-in token has no subject.");

    /// <summary>The id_token has no <c>username</c> claim.</summary>
    public static readonly Error UserNameMissing = new("User.UserNameMissing", "The sign-in token has no user name.");
}
