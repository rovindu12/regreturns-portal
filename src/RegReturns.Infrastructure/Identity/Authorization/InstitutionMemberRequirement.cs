using Microsoft.AspNetCore.Authorization;

using RegReturns.Application.Identity;

namespace RegReturns.Infrastructure.Identity.Authorization;

/// <summary>Requires an <c>institution_id</c> claim, so a bank role without a bank grants nothing.</summary>
public sealed class InstitutionMemberRequirement : IAuthorizationRequirement;

/// <summary>Checks for a non-empty <c>institution_id</c> claim.</summary>
public sealed class InstitutionMemberRequirementHandler : AuthorizationHandler<InstitutionMemberRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, InstitutionMemberRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!string.IsNullOrWhiteSpace(context.User.FindFirst(ClaimNames.InstitutionId)?.Value))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
