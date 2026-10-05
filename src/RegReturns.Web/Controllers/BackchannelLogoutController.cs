using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using RegReturns.Application.Auditing;
using RegReturns.Domain.Auditing;
using RegReturns.Web.Identity;

namespace RegReturns.Web.Controllers;

/// <summary>
/// OIDC back-channel logout (OIDC Back-Channel Logout 1.0): WSO2 posts a signed logout token when a user's WSO2
/// session ends, and the portal refuses every portal session carrying that <c>sid</c> from then on. Called server to
/// server, so it is anonymous and has no antiforgery token; the signed token is the proof.
/// </summary>
/// <param name="logger">The logger.</param>
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[Route(PortalPaths.BackchannelLogoutRoute)]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Security",
    "S4502:Disabling CSRF protections is security-sensitive",
    Justification = "Server-to-server endpoint called by WSO2 without browser cookies; a signed logout token is required instead.")]
public sealed partial class BackchannelLogoutController(ILogger<BackchannelLogoutController> logger) : ControllerBase
{
    /// <summary>The form field carrying the logout token.</summary>
    public const string LogoutTokenField = "logout_token";

    /// <summary>The audit details of a sign-out initiated by WSO2.</summary>
    public const string AuditDetails = "back-channel";

    private const int MaxRequestBytes = 16 * 1024;

    /// <summary>Ends the portal sessions of a WSO2 session. Routed as <c>Logout</c>.</summary>
    /// <param name="logoutToken">The logout token.</param>
    /// <param name="validator">Validates the logout token.</param>
    /// <param name="denyList">The ended sessions.</param>
    /// <param name="auditTrail">The audit trail.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>200 when the session was ended, otherwise 400.</returns>
    [HttpPost]
    [RequestSizeLimit(MaxRequestBytes)]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> LogoutAsync(
        [FromForm(Name = LogoutTokenField)] string? logoutToken,
        [FromServices] LogoutTokenValidator validator,
        [FromServices] ISessionDenyList denyList,
        [FromServices] IAuditTrail auditTrail,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(denyList);
        ArgumentNullException.ThrowIfNull(auditTrail);

        var result = await validator.ValidateAsync(logoutToken, cancellationToken);
        if (!result.IsValid)
        {
            LogLogoutTokenRejected(logger, result.FailureReason);
            return BadRequest(new { error = "invalid_request" });
        }

        await denyList.DenyAsync(result.SessionId!, cancellationToken);

        var origin = RequestOrigin.From(HttpContext);
        var actor = result.Subject is null ? AuditActor.Anonymous : new AuditActor(ActorType.User, result.Subject, null, null);
        await auditTrail.RecordAsync(actor.ToRecord(AuditAction.SignOut, AuditDetails, origin.IpAddress, origin.TraceId), cancellationToken);
        LogSessionEnded(logger, actor.SubjectId);
        return Ok();
    }

    [LoggerMessage(EventId = 3107, Level = LogLevel.Information, Message = "WSO2 ended the session of {SubjectId} through back-channel logout")]
    private static partial void LogSessionEnded(ILogger logger, string subjectId);

    [LoggerMessage(EventId = 3108, Level = LogLevel.Warning, Message = "Rejected a back-channel logout token: {Reason}")]
    private static partial void LogLogoutTokenRejected(ILogger logger, string? reason);
}
