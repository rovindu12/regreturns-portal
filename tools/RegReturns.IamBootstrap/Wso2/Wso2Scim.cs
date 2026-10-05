using System.Text.Json.Nodes;

namespace RegReturns.IamBootstrap.Wso2;

/// <summary>SCIM 2 helpers for users and application roles.</summary>
/// <param name="wso2">The WSO2 admin client.</param>
internal sealed class Wso2Scim(Wso2AdminClient wso2)
{
    /// <summary>SCIM core user schema.</summary>
    public const string UserSchema = "urn:ietf:params:scim:schemas:core:2.0:User";

    /// <summary>SCIM PATCH message schema.</summary>
    public const string PatchOpSchema = "urn:ietf:params:scim:api:messages:2.0:PatchOp";

    /// <summary>WSO2's SCIM role schema.</summary>
    public const string RoleSchema = "urn:ietf:params:scim:schemas:extension:2.0:Role";

    /// <summary>WSO2's own SCIM user extension (account flags such as <c>totpEnabled</c> and <c>accountLocked</c>).</summary>
    public const string Wso2UserSchema = "urn:scim:wso2:schema";

    private const string Users = "scim2/Users";
    private const string Roles = "scim2/v2/Roles";

    /// <summary>Finds a user by exact user name.</summary>
    /// <param name="userName">The user name.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The user resource, or <see langword="null"/>.</returns>
    public async Task<JsonNode?> FindUserAsync(string userName, CancellationToken cancellationToken)
    {
        var filter = Wso2Ids.Filter($"userName eq {userName}");
        var attributes = Wso2Ids.Filter($"userName,emails,{Wso2Ids.ScimCustomUserSchema}");
        var list = await wso2.GetAsync($"{Users}?filter={filter}&attributes={attributes}", cancellationToken);
        return list["Resources"]?.AsArray()
            .FirstOrDefault(u => string.Equals(u?["userName"]?.GetValue<string>(), userName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Creates a user and returns its id.</summary>
    /// <param name="body">The SCIM user (with password).</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The new user id.</returns>
    public async Task<string> CreateUserAsync(JsonObject body, CancellationToken cancellationToken)
    {
        var response = (await wso2.SendAsync(HttpMethod.Post, Users, body, Wso2AdminClient.ScimJson, cancellationToken)).EnsureSuccess();
        return response.Body?["id"]?.GetValue<string>() ?? throw new Wso2ApiException(response);
    }

    /// <summary>Applies SCIM PATCH operations to a user.</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="operations">The operations.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when done.</returns>
    public async Task PatchUserAsync(string userId, JsonArray operations, CancellationToken cancellationToken) =>
        (await wso2.SendAsync(HttpMethod.Patch, $"{Users}/{userId}", PatchOp(operations), Wso2AdminClient.ScimJson, cancellationToken))
            .EnsureSuccess();

    /// <summary>Returns whether WSO2 marks TOTP as enabled for a user (its <c>totpEnabled</c> claim).</summary>
    /// <param name="userId">The user id.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns><see langword="true"/> when the claim is <c>true</c>.</returns>
    public async Task<bool> IsTotpEnabledAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await wso2.GetAsync($"{Users}/{userId}", cancellationToken);
        return string.Equals(user[Wso2UserSchema]?["totpEnabled"]?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Lists the roles whose audience is the given application.</summary>
    /// <param name="appId">The application id.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Role ids by display name.</returns>
    public async Task<Dictionary<string, string>> ApplicationRolesAsync(string appId, CancellationToken cancellationToken)
    {
        var filter = Wso2Ids.Filter($"audience.value eq {appId}");
        var list = await wso2.GetAsync($"{Roles}?filter={filter}&count=100", cancellationToken);
        return (list["Resources"]?.AsArray() ?? [])
            .Where(r => r?["audience"]?["value"]?.GetValue<string>() == appId)
            .ToDictionary(r => r!["displayName"]!.GetValue<string>(), r => r!["id"]!.GetValue<string>(), StringComparer.Ordinal);
    }

    /// <summary>Creates an application-audience role without permissions.</summary>
    /// <param name="appId">The application id.</param>
    /// <param name="name">The role name.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The role id.</returns>
    public async Task<string> CreateApplicationRoleAsync(string appId, string name, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["schemas"] = new JsonArray(RoleSchema),
            ["displayName"] = name,
            ["audience"] = new JsonObject { ["type"] = "application", ["value"] = appId },
            ["permissions"] = new JsonArray(),
        };
        var response = (await wso2.SendAsync(HttpMethod.Post, Roles, body, Wso2AdminClient.ScimJson, cancellationToken)).EnsureSuccess();
        return response.Body?["id"]?.GetValue<string>() ?? throw new Wso2ApiException(response);
    }

    /// <summary>Returns the ids of the users assigned to a role.</summary>
    /// <param name="roleId">The role id.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The user ids.</returns>
    public async Task<HashSet<string>> RoleMembersAsync(string roleId, CancellationToken cancellationToken)
    {
        var role = await wso2.GetAsync($"{Roles}/{roleId}", cancellationToken);
        return (role["users"]?.AsArray() ?? [])
            .Select(u => u!["value"]!.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Adds and removes role members.</summary>
    /// <param name="roleId">The role id.</param>
    /// <param name="add">User ids to add.</param>
    /// <param name="remove">User ids to remove.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when done.</returns>
    public async Task UpdateRoleMembersAsync(
        string roleId, IReadOnlyCollection<string> add, IReadOnlyCollection<string> remove, CancellationToken cancellationToken)
    {
        var operations = new JsonArray();
        if (add.Count > 0)
        {
            operations.Add(new JsonObject
            {
                ["op"] = "add",
                ["path"] = "users",
                ["value"] = new JsonArray([.. add.Select(id => new JsonObject { ["value"] = id })]),
            });
        }

        foreach (var id in remove)
        {
            operations.Add(new JsonObject { ["op"] = "remove", ["path"] = $"users[value eq {id}]" });
        }

        if (operations.Count > 0)
        {
            (await wso2.SendAsync(HttpMethod.Patch, $"{Roles}/{roleId}", PatchOp(operations), Wso2AdminClient.ScimJson, cancellationToken))
                .EnsureSuccess();
        }
    }

    private static JsonObject PatchOp(JsonArray operations) => new()
    {
        ["schemas"] = new JsonArray(PatchOpSchema),
        ["Operations"] = operations,
    };
}
