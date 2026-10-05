using Microsoft.AspNetCore.Authorization;

using RegReturns.Application.Identity;

namespace RegReturns.Infrastructure.Identity.Authorization;

/// <summary>Requires an OAuth scope on a client-credentials token issued to a known client.</summary>
/// <param name="scope">The required scope.</param>
public sealed class ScopeRequirement(string scope) : IAuthorizationRequirement
{
    /// <summary>Gets the required scope.</summary>
    public string Scope { get; } = scope;
}

/// <summary>Checks the space-separated <c>scope</c> claim and the presence of a client id.</summary>
public sealed class ScopeRequirementHandler : AuthorizationHandler<ScopeRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ScopeRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);
        var hasClient = context.User.HasClaim(c => c.Type is ClaimNames.AuthorizedParty or ClaimNames.ClientId && c.Value.Length > 0);
        var hasScope = context.User.FindAll(ClaimNames.Scope)
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(requirement.Scope, StringComparer.Ordinal);

        if (hasClient && hasScope)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
