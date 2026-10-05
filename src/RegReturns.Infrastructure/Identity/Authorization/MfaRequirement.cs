using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

using RegReturns.Application.Identity;

namespace RegReturns.Infrastructure.Identity.Authorization;

/// <summary>Requires a TOTP sign-in when <see cref="IamOptions.EnforceMfa"/> is on.</summary>
public sealed class MfaRequirement : IAuthorizationRequirement;

/// <summary>Checks the <c>amr</c> claim for a TOTP method.</summary>
/// <param name="options">The IAM options (read on every check so the setting can change at runtime).</param>
public sealed class MfaRequirementHandler(IOptionsMonitor<IamOptions> options) : AuthorizationHandler<MfaRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, MfaRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = options.CurrentValue;
        var accepted = settings.AcceptedMfaAuthenticationMethods();
        var usedMfa = context.User.FindAll(ClaimNames.AuthenticationMethods)
            .Any(claim => accepted.Contains(claim.Value, StringComparer.OrdinalIgnoreCase));

        if (!settings.EnforceMfa || usedMfa)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
