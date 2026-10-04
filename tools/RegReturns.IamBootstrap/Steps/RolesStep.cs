using RegReturns.Application.Identity;
using RegReturns.IamBootstrap.Wso2;

namespace RegReturns.IamBootstrap.Steps;

/// <summary>Ensures the six RegReturns roles exist with the portal as their audience, so they appear in its tokens only.</summary>
/// <param name="scim">SCIM helpers.</param>
internal sealed class RolesStep(Wso2Scim scim) : IBootstrapStep
{
    /// <inheritdoc />
    public string Name => "roles";

    /// <inheritdoc />
    public async Task RunAsync(BootstrapState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        var appId = state.PortalAppId ?? throw new InvalidOperationException("The portal app step must run first.");
        var existing = await scim.ApplicationRolesAsync(appId, cancellationToken);
        foreach (var role in RoleNames.All)
        {
            if (existing.TryGetValue(role, out var id))
            {
                state.RoleIds[role] = id;
                state.Record("role", role, Outcome.Unchanged);
                continue;
            }

            state.RoleIds[role] = await scim.CreateApplicationRoleAsync(appId, role, cancellationToken);
            state.Record("role", role, Outcome.Created);
        }
    }
}
