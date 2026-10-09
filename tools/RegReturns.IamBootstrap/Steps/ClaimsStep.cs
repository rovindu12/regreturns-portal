using System.Net;
using System.Text.Json.Nodes;

using RegReturns.Application.Identity;
using RegReturns.IamBootstrap.Wso2;

namespace RegReturns.IamBootstrap.Steps;

/// <summary>
/// Ensures the <c>institution_id</c> attribute end to end (local claim, OIDC claim, SCIM custom-schema attribute and
/// the <c>institution</c> OIDC scope, in that order, each depending on the one before), a SCIM attribute for the
/// full name that WSO2 releases as the OIDC <c>name</c> claim, and the TOTP enrolment window (local claim and SCIM
/// attribute, never released in tokens; ADR 0032).
/// </summary>
/// <param name="wso2">The WSO2 admin client.</param>
internal sealed class ClaimsStep(Wso2AdminClient wso2) : IBootstrapStep
{
    private const string ClaimDialects = "api/server/v1/claim-dialects";
    private const string Scopes = "api/server/v1/oidc/scopes";

    private static readonly LocalClaim Institution = new(
        IamNames.InstitutionLocalClaim, IamNames.InstitutionStoreAttribute, "Institution",
        "Code of the bank a RegReturns user works for. Empty for regulator staff. Set by administrators only.",
        SupportedByDefault: true);

    private static readonly LocalClaim TotpEnrolment = new(
        TotpEnrolmentClaim.LocalClaim, TotpEnrolmentClaim.StoreAttribute, "TOTP enrolment until",
        "End of the window (Unix milliseconds) in which the user sets up a new authenticator at sign-in. Set by the RegReturns portal for an administrator.",
        SupportedByDefault: false);

    /// <inheritdoc />
    public string Name => "claims";

    /// <inheritdoc />
    public async Task RunAsync(BootstrapState state, CancellationToken cancellationToken)
    {
        state.Record("local claim", IamNames.InstitutionLocalClaim, await EnsureLocalClaimAsync(Institution, cancellationToken));
        state.Record("OIDC claim", IamNames.InstitutionOidcClaim, await EnsureExternalClaimAsync(
            Wso2Ids.OidcDialectUri, IamNames.InstitutionOidcClaim, IamNames.InstitutionLocalClaim, cancellationToken));
        await EnsureDialectAsync(Wso2Ids.ScimCustomUserSchema, cancellationToken);
        var institution = $"{Wso2Ids.ScimCustomUserSchema}:{IamNames.InstitutionScimAttribute}";
        state.Record("SCIM attribute", institution, await EnsureExternalClaimAsync(
            Wso2Ids.ScimCustomUserSchema, institution, IamNames.InstitutionLocalClaim, cancellationToken));

        // OIDC "name" comes from the full-name claim, which has no SCIM attribute by default.
        var fullName = $"{Wso2Ids.ScimCustomUserSchema}:{IamNames.FullNameScimAttribute}";
        state.Record("SCIM attribute", fullName, await EnsureExternalClaimAsync(
            Wso2Ids.ScimCustomUserSchema, fullName, Wso2Ids.FullNameClaim, cancellationToken));
        state.Record("OIDC scope", IamNames.InstitutionScope, await EnsureScopeAsync(cancellationToken));

        state.Record("local claim", TotpEnrolmentClaim.LocalClaim, await EnsureLocalClaimAsync(TotpEnrolment, cancellationToken));
        var enrolment = $"{Wso2Ids.ScimCustomUserSchema}:{TotpEnrolmentClaim.ScimAttribute}";
        state.Record("SCIM attribute", enrolment, await EnsureExternalClaimAsync(
            Wso2Ids.ScimCustomUserSchema, enrolment, TotpEnrolmentClaim.LocalClaim, cancellationToken));
    }

    private async Task<Outcome> EnsureLocalClaimAsync(LocalClaim claim, CancellationToken ct)
    {
        var desired = new JsonObject
        {
            ["claimURI"] = claim.Uri,
            ["displayName"] = claim.DisplayName,
            ["description"] = claim.Description,
            ["supportedByDefault"] = claim.SupportedByDefault,
            ["readOnly"] = true,
            ["required"] = false,
            ["attributeMapping"] = new JsonArray(new JsonObject
            {
                ["mappedAttribute"] = claim.StoreAttribute,
                ["userstore"] = "PRIMARY",
            }),
        };

        var path = $"{ClaimDialects}/{Wso2Ids.LocalDialect}/claims";
        var id = Wso2Ids.ForUri(claim.Uri);
        var existing = await wso2.GetOrDefaultAsync($"{path}/{id}", ct);
        if (existing is null)
        {
            (await wso2.SendAsync(HttpMethod.Post, path, desired, Wso2AdminClient.Json, ct)).EnsureSuccess();
            return Outcome.Created;
        }

        if (Matches(existing, desired, claim.StoreAttribute))
        {
            return Outcome.Unchanged;
        }

        // PUT replaces the whole claim: always send the complete desired state.
        (await wso2.SendAsync(HttpMethod.Put, $"{path}/{id}", desired, Wso2AdminClient.Json, ct)).EnsureSuccess();
        return Outcome.Updated;
    }

    private async Task<Outcome> EnsureExternalClaimAsync(string dialectUri, string claimUri, string localClaimUri, CancellationToken ct)
    {
        var path = $"{ClaimDialects}/{Wso2Ids.ForUri(dialectUri)}/claims";
        var id = Wso2Ids.ForUri(claimUri);
        var desired = new JsonObject { ["claimURI"] = claimUri, ["mappedLocalClaimURI"] = localClaimUri };

        var existing = await wso2.GetOrDefaultAsync($"{path}/{id}", ct);
        if (existing is null)
        {
            // 400 CMT-60004 means another name in this dialect already maps the local claim: a manual change to fix by hand.
            (await wso2.SendAsync(HttpMethod.Post, path, desired, Wso2AdminClient.Json, ct)).EnsureSuccess();
            return Outcome.Created;
        }

        if (existing["mappedLocalClaimURI"]?.GetValue<string>() == localClaimUri)
        {
            return Outcome.Unchanged;
        }

        (await wso2.SendAsync(HttpMethod.Put, $"{path}/{id}", desired, Wso2AdminClient.Json, ct)).EnsureSuccess();
        return Outcome.Updated;
    }

    private async Task EnsureDialectAsync(string dialectUri, CancellationToken ct)
    {
        if (await wso2.GetOrDefaultAsync($"{ClaimDialects}/{Wso2Ids.ForUri(dialectUri)}", ct) is not null)
        {
            return;
        }

        // 409 CMT-60002: created concurrently, which is fine. The dialect is tenant-wide and never deleted.
        (await wso2.SendAsync(HttpMethod.Post, ClaimDialects, new JsonObject { ["dialectURI"] = dialectUri }, Wso2AdminClient.Json, ct))
            .EnsureSuccess(HttpStatusCode.Conflict);
    }

    private async Task<Outcome> EnsureScopeAsync(CancellationToken ct)
    {
        const string displayName = "Institution";
        const string description = "The bank the user works for (RegReturns).";
        var claims = new[] { IamNames.InstitutionOidcClaim };

        var existing = await wso2.GetOrDefaultAsync($"{Scopes}/{IamNames.InstitutionScope}", ct);
        if (existing is null)
        {
            var create = new JsonObject
            {
                ["name"] = IamNames.InstitutionScope,
                ["displayName"] = displayName,
                ["description"] = description,
                ["claims"] = new JsonArray([.. claims.Select(c => JsonValue.Create(c))]),
            };
            (await wso2.SendAsync(HttpMethod.Post, Scopes, create, Wso2AdminClient.Json, ct)).EnsureSuccess();
            return Outcome.Created;
        }

        var currentClaims = existing["claims"]?.AsArray().Select(c => c!.GetValue<string>()).ToHashSet(StringComparer.Ordinal) ?? [];
        if (existing["displayName"]?.GetValue<string>() == displayName &&
            existing["description"]?.GetValue<string>() == description &&
            currentClaims.SetEquals(claims))
        {
            return Outcome.Unchanged;
        }

        // PUT replaces the claim list and must not contain "name".
        var update = new JsonObject
        {
            ["displayName"] = displayName,
            ["description"] = description,
            ["claims"] = new JsonArray([.. claims.Select(c => JsonValue.Create(c))]),
        };
        (await wso2.SendAsync(HttpMethod.Put, $"{Scopes}/{IamNames.InstitutionScope}", update, Wso2AdminClient.Json, ct)).EnsureSuccess();
        return Outcome.Updated;
    }

    private static bool Matches(JsonNode existing, JsonObject desired, string storeAttribute)
    {
        foreach (var (key, value) in desired)
        {
            if (key == "attributeMapping")
            {
                var mapping = existing["attributeMapping"]?.AsArray()
                    .Any(m => string.Equals(m?["userstore"]?.GetValue<string>(), "PRIMARY", StringComparison.OrdinalIgnoreCase) &&
                              m?["mappedAttribute"]?.GetValue<string>() == storeAttribute) ?? false;
                if (!mapping)
                {
                    return false;
                }
            }
            else if (!JsonNode.DeepEquals(existing[key], value))
            {
                return false;
            }
        }

        return true;
    }

    // A RegReturns local claim: read-only for users (only administrators and RegReturns set it), in the primary store.
    private sealed record LocalClaim(string Uri, string StoreAttribute, string DisplayName, string Description, bool SupportedByDefault);
}
