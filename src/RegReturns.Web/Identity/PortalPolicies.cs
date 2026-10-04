namespace RegReturns.Web.Identity;

/// <summary>
/// Portal-only policy names for endpoints that any signed-in user may call (for example signing out). The business
/// policies live in <see cref="RegReturns.Application.Authorization.Policies"/>.
/// </summary>
public static class PortalPolicies
{
    /// <summary>Any authenticated user, named so that every endpoint states its policy explicitly.</summary>
    public const string SignedIn = "Portal.SignedIn";
}
