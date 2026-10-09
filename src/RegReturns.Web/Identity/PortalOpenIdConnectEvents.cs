using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

using RegReturns.Domain.Auditing;
using RegReturns.Infrastructure.Auditing;

namespace RegReturns.Web.Identity;

/// <summary>
/// OpenID Connect events for the portal: hands validated tokens to <see cref="SignInProcessor"/>, keeps only the
/// id_token (for <c>id_token_hint</c> at logout), and turns every remote failure into a friendly page with an error
/// reference and an <see cref="AuditAction.AuthenticationFailed"/> entry.
/// </summary>
/// <param name="processor">Normalises, links and audits the sign-in.</param>
/// <param name="failureAuditor">Records failed sign-ins, de-duplicated per minute.</param>
/// <param name="logger">The logger.</param>
public sealed partial class PortalOpenIdConnectEvents(
    SignInProcessor processor,
    AccessDeniedAuditor failureAuditor,
    ILogger<PortalOpenIdConnectEvents> logger) : OpenIdConnectEvents
{
    /// <summary>The authentication property that carries a user name to fill in on WSO2's sign-in page.</summary>
    public const string LoginHintItem = "regreturns.login_hint";

    /// <summary>Returns whether a value may be sent as <c>login_hint</c>: a plausible user name, nothing else.</summary>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> when it may be sent.</returns>
    public static bool IsLoginHint([NotNullWhen(true)] string? value) => value is not null && LoginHintPattern().IsMatch(value);

    /// <summary>Adds the requested <c>login_hint</c>, so WSO2's sign-in page starts with that user name.</summary>
    /// <param name="context">The redirect context.</param>
    /// <returns>A completed task.</returns>
    public override Task RedirectToIdentityProvider(RedirectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Properties.Items.TryGetValue(LoginHintItem, out var hint) && IsLoginHint(hint))
        {
            context.ProtocolMessage.LoginHint = hint;
        }

        return base.RedirectToIdentityProvider(context);
    }

    /// <inheritdoc />
    public override async Task TokenValidated(TokenValidatedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Principal is null)
        {
            context.Fail(new SignInRejectedException(SignInErrors.SubjectMissing.Code));
            return;
        }

        var result = await processor.ProcessAsync(context.Principal, RequestOrigin.From(context.HttpContext), context.HttpContext.RequestAborted);
        if (result.IsFailure)
        {
            context.Fail(new SignInRejectedException(result.Error!.Code));
            return;
        }

        context.Principal = result.Value;

        // SaveTokens is off: the access token is never needed, and only the id_token is kept for id_token_hint.
        var idToken = context.TokenEndpointResponse?.IdToken ?? context.ProtocolMessage?.IdToken;
        if (!string.IsNullOrEmpty(idToken) && context.Properties is not null)
        {
            context.Properties.StoreTokens([new AuthenticationToken { Name = OpenIdConnectParameterNames.IdToken, Value = idToken }]);
        }

        await base.TokenValidated(context);
    }

    /// <inheritdoc />
    public override async Task RemoteFailure(RemoteFailureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var origin = RequestOrigin.From(context.HttpContext);
        var code = SignInFailureCodes.From(context.Failure);

        // A refusal by SignInProcessor has already been logged and audited with the user as the actor.
        if (context.Failure is not SignInRejectedException)
        {
            LogRemoteFailure(logger, context.Failure, code);
            await failureAuditor.RecordAsync(
                null, AuditAction.AuthenticationFailed, origin.Path, code, origin.IpAddress, origin.TraceId, context.HttpContext.RequestAborted);
        }

        context.HandleResponse();
        context.Response.Redirect(SignInFailedUrl(code, origin.TraceId));
    }

    /// <summary>Builds the failure page URL carrying the reason code and the error reference.</summary>
    /// <param name="code">The failure code.</param>
    /// <param name="traceId">The trace id of the failed request.</param>
    /// <returns>The local URL of the failure page.</returns>
    public static string SignInFailedUrl(string code, string traceId) =>
        QueryHelpers.AddQueryString(PortalPaths.SignInFailed, new Dictionary<string, string?>
        {
            [PortalPaths.ReasonParameter] = code,
            [PortalPaths.ReferenceParameter] = traceId,
        });

    [LoggerMessage(EventId = 3104, Level = LogLevel.Warning, Message = "Sign-in through WSO2 failed: {FailureCode}")]
    private static partial void LogRemoteFailure(ILogger logger, Exception? exception, string failureCode);

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LoginHintPattern();
}
