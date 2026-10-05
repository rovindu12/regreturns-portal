using System.Text.Json.Nodes;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using RegReturns.Application.Identity;
using RegReturns.IamBootstrap.Wso2;
using RegReturns.Infrastructure.Persistence;

namespace RegReturns.IamBootstrap.Steps;

/// <summary>
/// Creates the demo users in WSO2 from the demo <c>AppUser</c> rows (the single list of demo accounts), keeps their
/// e-mail, full name, institution and roles in line, and links each <c>AppUser</c> to its WSO2 user id. Passwords are
/// set on creation and reset only by the <c>demo-users</c> command (<see cref="BootstrapState.ResetDemoUsers"/>).
/// </summary>
/// <param name="scim">SCIM helpers.</param>
/// <param name="applications">Application management, to find the portal when run on its own.</param>
/// <param name="db">The RegReturns database.</param>
/// <param name="options">The tool options.</param>
internal sealed class DemoUsersStep(Wso2Scim scim, Wso2Applications applications, RegReturnsDbContext db, IOptions<BootstrapOptions> options)
    : IBootstrapStep
{
    /// <summary>The step name used by the <c>demo-users</c> command.</summary>
    public const string StepName = "demo-users";

    /// <inheritdoc />
    public string Name => StepName;

    /// <inheritdoc />
    public async Task RunAsync(BootstrapState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        await EnsureRoleIdsAsync(state, cancellationToken);

        var users = await (
            from user in db.Users
            where user.IsDemoAccount
            join institution in db.Institutions on user.InstitutionId equals institution.Id into banks
            from bank in banks.DefaultIfEmpty()
            orderby user.UserName
            select new { User = user, InstitutionCode = bank == null ? null : bank.Code })
            .ToListAsync(cancellationToken);

        var desiredMembers = RoleNames.All.ToDictionary(r => r, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var demoUserIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in users)
        {
            var user = entry.User;
            var email = user.Email ?? throw new InvalidOperationException($"Demo user '{user.UserName}' has no e-mail address.");
            var userId = await EnsureUserAsync(user.UserName, user.DisplayName, email, entry.InstitutionCode, state, cancellationToken);
            state.UserIds[user.UserName] = userId;
            state.DemoUserRoles[user.UserName] = [.. user.Roles.Select(RoleNames.For)];
            demoUserIds.Add(userId);
            foreach (var role in user.Roles)
            {
                desiredMembers[RoleNames.For(role)].Add(userId);
            }

            if (user.Wso2UserId != userId)
            {
                user.LinkIdentity(userId);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        // Only demo users are added or removed; users created by an administrator keep their roles.
        foreach (var (role, roleId) in state.RoleIds)
        {
            var current = await scim.RoleMembersAsync(roleId, cancellationToken);
            var add = desiredMembers[role].Where(id => !current.Contains(id)).ToList();
            var remove = current.Where(id => demoUserIds.Contains(id) && !desiredMembers[role].Contains(id)).ToList();
            await scim.UpdateRoleMembersAsync(roleId, add, remove, cancellationToken);
            state.Record("role members", role, add.Count + remove.Count == 0 ? Outcome.Unchanged : Outcome.Updated);
        }
    }

    private async Task EnsureRoleIdsAsync(BootstrapState state, CancellationToken ct)
    {
        if (state.RoleIds.Count == RoleNames.All.Count)
        {
            return;
        }

        var portal = await applications.FindAsync(IamNames.PortalApp, ct)
            ?? throw new InvalidOperationException("The portal app does not exist yet; run `apply` first.");
        var roles = await scim.ApplicationRolesAsync(portal["id"]!.GetValue<string>(), ct);
        foreach (var role in RoleNames.All)
        {
            state.RoleIds[role] = roles.TryGetValue(role, out var id)
                ? id
                : throw new InvalidOperationException($"Role '{role}' does not exist yet; run `apply` first.");
        }
    }

    private async Task<string> EnsureUserAsync(
        string userName, string displayName, string email, string? institutionCode, BootstrapState state, CancellationToken ct)
    {
        var existing = await scim.FindUserAsync(userName, ct);
        if (existing is null)
        {
            var custom = new JsonObject { [IamNames.FullNameScimAttribute] = displayName };
            if (institutionCode is not null)
            {
                custom[IamNames.InstitutionScimAttribute] = institutionCode;
            }

            var body = new JsonObject
            {
                ["schemas"] = new JsonArray(Wso2Scim.UserSchema, Wso2Ids.ScimCustomUserSchema),
                ["userName"] = userName,
                ["password"] = options.Value.DemoUserPassword,
                ["emails"] = new JsonArray(new JsonObject { ["value"] = email, ["primary"] = true }),
                [Wso2Ids.ScimCustomUserSchema] = custom,
            };
            var id = await scim.CreateUserAsync(body, ct);
            state.Record("demo user", userName, Outcome.Created);
            return id;
        }

        var userId = existing["id"]!.GetValue<string>();
        var operations = new JsonArray();
        var currentCustom = existing[Wso2Ids.ScimCustomUserSchema];
        var currentEmail = existing["emails"]?.AsArray().Select(e => e is JsonObject o ? o["value"]?.GetValue<string>() : e?.GetValue<string>())
            .FirstOrDefault();

        if (currentEmail != email)
        {
            operations.Add(Replace("emails", new JsonArray(new JsonObject { ["value"] = email, ["primary"] = true })));
        }

        if (currentCustom?[IamNames.FullNameScimAttribute]?.GetValue<string>() != displayName)
        {
            operations.Add(Replace($"{Wso2Ids.ScimCustomUserSchema}:{IamNames.FullNameScimAttribute}", displayName));
        }

        var currentInstitution = currentCustom?[IamNames.InstitutionScimAttribute]?.GetValue<string>();
        if (currentInstitution != institutionCode)
        {
            operations.Add(institutionCode is null
                ? new JsonObject { ["op"] = "remove", ["path"] = $"{Wso2Ids.ScimCustomUserSchema}:{IamNames.InstitutionScimAttribute}" }
                : Replace($"{Wso2Ids.ScimCustomUserSchema}:{IamNames.InstitutionScimAttribute}", institutionCode));
        }

        if (state.ResetDemoUsers)
        {
            operations.Add(Replace("password", options.Value.DemoUserPassword!));
        }

        if (operations.Count > 0)
        {
            await scim.PatchUserAsync(userId, operations, ct);
        }

        state.Record("demo user", userName, operations.Count == 0 ? Outcome.Unchanged : Outcome.Updated);
        return userId;
    }

    private static JsonObject Replace(string path, JsonNode value) => new() { ["op"] = "replace", ["path"] = path, ["value"] = value };
}
