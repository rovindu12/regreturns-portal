using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using RegReturns.Application.Auditing;
using RegReturns.Domain.Auditing;
using RegReturns.Web.Identity;
using RegReturns.Web.Models;

namespace RegReturns.Web.Controllers;

/// <summary>Signing in and out through WSO2, and the pages around it.</summary>
/// <param name="logger">The logger.</param>
[Route(PortalPaths.AccountPrefix)]
public sealed partial class AccountController(ILogger<AccountController> logger) : Controller
{
    /// <summary>The audit details of a sign-out the user started in the portal.</summary>
    public const string SignOutAuditDetails = "portal";

    /// <summary>Starts a WSO2 sign-in, returning to a local URL afterwards.</summary>
    /// <param name="returnUrl">Where to go after signing in; anything not local is replaced by the home page.</param>
    /// <returns>A challenge, or a redirect when already signed in.</returns>
    [HttpGet(PortalPaths.SignInAction)]
    [AllowAnonymous]
    public IActionResult SignIn(string? returnUrl)
    {
        var target = returnUrl is not null && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
        return User.Identity?.IsAuthenticated == true
            ? LocalRedirect(target)
            : Challenge(new AuthenticationProperties { RedirectUri = target });
    }

    /// <summary>
    /// Records the sign-out, ends the portal session and sends the browser to WSO2's end-session endpoint with
    /// <c>id_token_hint</c>, which returns to <see cref="SignedOut"/>. Routed as <c>SignOut</c>.
    /// </summary>
    /// <param name="auditTrail">The audit trail.</param>
    /// <param name="denyList">Revoked sessions; the signed-out cookie ticket is added so a copy of it stops working.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The sign-out result for the cookie and OpenID Connect schemes.</returns>
    [HttpPost(PortalPaths.SignOutAction)]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PortalPolicies.SignedIn)]
    public async Task<IActionResult> SignOutAsync(
        [FromServices] IAuditTrail auditTrail, [FromServices] ISessionDenyList denyList, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditTrail);
        ArgumentNullException.ThrowIfNull(denyList);

        // A copy of this cookie must stop working even if WSO2's back-channel logout never arrives.
        var ticket = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (ticket.Properties is not null && PortalSession.TicketDenyKey(ticket.Properties) is { } ticketKey)
        {
            await denyList.DenyAsync(ticketKey, cancellationToken);
        }

        var origin = RequestOrigin.From(HttpContext);
        var actor = AuditActor.FromPrincipal(User);
        try
        {
            await auditTrail.RecordAsync(actor.ToRecord(AuditAction.SignOut, SignOutAuditDetails, origin.IpAddress, origin.TraceId), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Signing out must always work; a lost audit entry is logged with the trace id instead.
            LogSignOutAuditFailed(logger, ex, actor.SubjectId);
        }

        LogSignedOut(logger, actor.SubjectId);
        return SignOut(
            new AuthenticationProperties { RedirectUri = PortalPaths.SignedOut },
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);
    }

    /// <summary>Shown when a signed-in user is refused by a policy.</summary>
    /// <returns>The access-denied page.</returns>
    [HttpGet(PortalPaths.AccessDeniedAction)]
    [AllowAnonymous]
    public IActionResult AccessDenied() => View();

    /// <summary>Shown after WSO2 has ended the session.</summary>
    /// <returns>The signed-out page.</returns>
    [HttpGet(PortalPaths.SignedOutAction)]
    [AllowAnonymous]
    public IActionResult SignedOut() => View();

    /// <summary>Shown when sign-in failed, with a plain-words reason and the error reference.</summary>
    /// <param name="reason">The failure code.</param>
    /// <param name="reference">The trace id of the failed request.</param>
    /// <returns>The failure page.</returns>
    [HttpGet(PortalPaths.SignInFailedAction)]
    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult SignInFailed(
        [FromQuery(Name = PortalPaths.ReasonParameter)] string? reason,
        [FromQuery(Name = PortalPaths.ReferenceParameter)] string? reference) =>
        View(SignInFailedViewModel.Create(reason, reference));

    [LoggerMessage(EventId = 3111, Level = LogLevel.Information, Message = "User {SubjectId} signed out")]
    private static partial void LogSignedOut(ILogger logger, string subjectId);

    [LoggerMessage(EventId = 3112, Level = LogLevel.Error, Message = "Could not write the sign-out audit entry for {SubjectId}")]
    private static partial void LogSignOutAuditFailed(ILogger logger, Exception exception, string subjectId);
}
