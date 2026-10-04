using System.Text.Json.Nodes;

using RegReturns.Application.Identity;
using RegReturns.IamBootstrap.Wso2;

namespace RegReturns.IamBootstrap.Steps;

/// <summary>Ensures the RegReturns API resource and its scopes. Scopes are only ever added, never replaced (a PUT would un-authorize them from every app).</summary>
/// <param name="wso2">The WSO2 admin client.</param>
internal sealed class ApiResourceStep(Wso2AdminClient wso2) : IBootstrapStep
{
    private const string ApiResources = "api/server/v1/api-resources";

    private static readonly (string Name, string DisplayName, string Description)[] DesiredScopes =
    [
        (ApiScopes.ReturnsRead, "Read returns", "Read the calling bank's returns and their status."),
        (ApiScopes.ReturnsSubmit, "Submit returns", "Create and submit returns for the calling bank."),
        (ApiScopes.ReferenceRead, "Read reference data", "Read the calling bank's institution and return types."),
    ];

    /// <inheritdoc />
    public string Name => "api-resource";

    /// <inheritdoc />
    public async Task RunAsync(BootstrapState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        var list = await wso2.GetAsync($"{ApiResources}?filter=identifier+eq+{Wso2Ids.Filter(ApiScopes.ApiIdentifier)}", cancellationToken);
        var existing = list["apiResources"]?.AsArray().FirstOrDefault();
        if (existing is null)
        {
            var create = new JsonObject
            {
                ["name"] = IamNames.ApiResourceName,
                ["identifier"] = ApiScopes.ApiIdentifier,
                ["description"] = "Machine-to-machine API used by bank systems.",
                ["requiresAuthorization"] = true,
                ["scopes"] = new JsonArray([.. DesiredScopes.Select(ToJson)]),
            };
            var created = (await wso2.SendAsync(HttpMethod.Post, ApiResources, create, Wso2AdminClient.Json, cancellationToken)).EnsureSuccess();
            state.ApiResourceId = created.Body?["id"]?.GetValue<string>() ?? created.LocationId;
            state.Record("API resource", ApiScopes.ApiIdentifier, Outcome.Created);
            return;
        }

        state.ApiResourceId = existing["id"]!.GetValue<string>();
        var scopes = await wso2.GetAsync($"{ApiResources}/{state.ApiResourceId}/scopes", cancellationToken);
        var have = scopes.AsArray().Select(s => s!["name"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var missing = DesiredScopes.Where(s => !have.Contains(s.Name)).ToList();
        if (missing.Count == 0)
        {
            state.Record("API resource", ApiScopes.ApiIdentifier, Outcome.Unchanged);
            return;
        }

        var patch = new JsonObject { ["addedScopes"] = new JsonArray([.. missing.Select(ToJson)]) };
        (await wso2.SendAsync(HttpMethod.Patch, $"{ApiResources}/{state.ApiResourceId}", patch, Wso2AdminClient.Json, cancellationToken))
            .EnsureSuccess();
        state.Record("API resource", ApiScopes.ApiIdentifier, Outcome.Updated);
    }

    private static JsonObject ToJson((string Name, string DisplayName, string Description) scope) => new()
    {
        ["name"] = scope.Name,
        ["displayName"] = scope.DisplayName,
        ["description"] = scope.Description,
    };
}
