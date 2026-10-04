namespace RegReturns.Web.Identity;

/// <summary>
/// Route segments and paths of the sign-in flow. The OIDC callback paths are registered on the portal application
/// in WSO2 relative to the portal's public base URL.
/// </summary>
public static class PortalPaths
{
    /// <summary>Route prefix of <c>AccountController</c>.</summary>
    public const string AccountPrefix = "Account";

    /// <summary>Route segment that starts a sign-in.</summary>
    public const string SignInAction = "SignIn";

    /// <summary>Route segment that signs out.</summary>
    public const string SignOutAction = "SignOut";

    /// <summary>Route segment of the access-denied page.</summary>
    public const string AccessDeniedAction = "AccessDenied";

    /// <summary>Route segment of the signed-out page.</summary>
    public const string SignedOutAction = "SignedOut";

    /// <summary>Route segment of the sign-in failure page.</summary>
    public const string SignInFailedAction = "SignInFailed";

    /// <summary>Route of the back-channel logout endpoint (no leading slash).</summary>
    public const string BackchannelLogoutRoute = "signout-backchannel";

    /// <summary>Starts a sign-in; accepts a local <c>returnUrl</c>.</summary>
    public const string SignIn = "/" + AccountPrefix + "/" + SignInAction;

    /// <summary>Signs out (POST with an antiforgery token).</summary>
    public const string SignOut = "/" + AccountPrefix + "/" + SignOutAction;

    /// <summary>Shown when a signed-in user is refused by a policy.</summary>
    public const string AccessDenied = "/" + AccountPrefix + "/" + AccessDeniedAction;

    /// <summary>Shown after WSO2 has ended the session.</summary>
    public const string SignedOut = "/" + AccountPrefix + "/" + SignedOutAction;

    /// <summary>Shown when sign-in fails, with a reason code and an error reference.</summary>
    public const string SignInFailed = "/" + AccountPrefix + "/" + SignInFailedAction;

    /// <summary>The OIDC redirect URI (authorization response).</summary>
    public const string SignInCallback = "/signin-oidc";

    /// <summary>The OIDC post-logout redirect URI.</summary>
    public const string SignedOutCallback = "/signout-callback-oidc";

    /// <summary>The OIDC back-channel logout URI that WSO2 posts logout tokens to.</summary>
    public const string BackchannelLogout = "/" + BackchannelLogoutRoute;

    /// <summary>Query parameter carrying the failure reason code to <see cref="SignInFailed"/>.</summary>
    public const string ReasonParameter = "reason";

    /// <summary>Query parameter carrying the trace id of the failed request to <see cref="SignInFailed"/>.</summary>
    public const string ReferenceParameter = "reference";
}
