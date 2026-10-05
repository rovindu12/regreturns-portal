extern alias ApiHost;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using ApiHost::RegReturns.Api.Contracts;

using RegReturns.Application.Identity;
using RegReturns.Infrastructure.Persistence.Seeding;
using RegReturns.IntegrationTests.Hosting;
using RegReturns.IntegrationTests.Infrastructure;

namespace RegReturns.IntegrationTests.Api;

/// <summary>Institution scoping of the first API endpoints, with callers set up through the test scheme.</summary>
[Collection(HostedAppsDefinition.Name)]
public sealed class InstitutionEndpointTests(TestSchemeApiFixture api) : IClassFixture<TestSchemeApiFixture>
{
    private const string ProblemJson = "application/problem+json";

    [Fact]
    public async Task Me_describes_the_calling_client_its_institution_and_scopes()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientId = await api.Database.RegisterClientAsync(DemoBank.Harbourline, active: true, ct);
        using var client = ClientAs(clientId, $"{ApiScopes.ReturnsRead} {ApiScopes.ReferenceRead}");

        var me = await client.GetFromJsonAsync<MeResponse>("/v1/me", ct);

        me.ShouldNotBeNull();
        me.ClientId.ShouldBe(clientId);
        me.Institution.ShouldBe(new InstitutionResponse(DemoBank.Harbourline, "Harbourline Bank PLC", "Commercial"));
        me.Scopes.ShouldBe([ApiScopes.ReferenceRead, ApiScopes.ReturnsRead]);
    }

    [Fact]
    public async Task Own_institution_is_returned_whatever_the_case_of_the_code()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientId = await api.Database.RegisterClientAsync(DemoBank.Northgate, active: true, ct);
        using var client = ClientAs(clientId);

        var institution = await client.GetFromJsonAsync<InstitutionResponse>("/v1/institutions/nsb", ct);

        institution.ShouldBe(new InstitutionResponse(DemoBank.Northgate, "Northgate Savings Bank", "Savings"));
    }

    [Fact]
    public async Task Another_banks_code_gets_the_same_not_found_problem_as_an_unknown_code()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientId = await api.Database.RegisterClientAsync(DemoBank.Harbourline, active: true, ct);
        using var client = ClientAs(clientId);

        var otherBank = await client.GetAsync($"/v1/institutions/{DemoBank.Crestmont}", ct);
        var unknown = await client.GetAsync("/v1/institutions/NOPE", ct);

        otherBank.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        otherBank.Content.Headers.ContentType!.MediaType.ShouldBe(ProblemJson);
        var otherBody = await ReadProblemAsync(otherBank, ct);
        otherBody.ShouldBe(await ReadProblemAsync(unknown, ct));
        otherBody.ShouldNotContain("Crestmont");
    }

    [Fact]
    public async Task A_client_without_the_reference_scope_is_forbidden()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientId = await api.Database.RegisterClientAsync(DemoBank.Harbourline, active: true, ct);
        using var client = ClientAs(clientId, ApiScopes.ReturnsRead);

        var response = await client.GetAsync("/v1/me", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.ShouldBe(ProblemJson);
    }

    [Fact]
    public async Task A_client_missing_from_the_registry_is_forbidden()
    {
        using var client = ClientAs(ApiDatabase.UnregisteredClientId(DemoBank.Harbourline));

        var response = await client.GetAsync("/v1/me", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_inactive_client_is_forbidden()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientId = await api.Database.RegisterClientAsync(DemoBank.LotusUnion, active: false, ct);
        using var client = ClientAs(clientId);

        var response = await client.GetAsync($"/v1/institutions/{DemoBank.LotusUnion}", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_institution_claim_carried_in_the_token_grants_nothing()
    {
        var clientId = ApiDatabase.UnregisteredClientId(DemoBank.Harbourline);
        using var client = api.Factory.CreateClientAs([.. ClientClaims(clientId, ApiScopes.ReferenceRead), (ClaimNames.InstitutionId, DemoBank.Harbourline)]);

        var response = await client.GetAsync($"/v1/institutions/{DemoBank.Harbourline}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private HttpClient ClientAs(string clientId, string scope = ApiScopes.ReferenceRead) =>
        api.Factory.CreateClientAs(ClientClaims(clientId, scope));

    private static (string Type, string Value)[] ClientClaims(string clientId, string scope) =>
    [
        (ClaimNames.AuthorizedUserType, ClaimNames.ApplicationTokenType),
        (ClaimNames.Subject, clientId),
        (ClaimNames.AuthorizedParty, clientId),
        (ClaimNames.ClientId, clientId),
        (ClaimNames.Scope, scope),
    ];

    /// <summary>Reads a problem body without the members that legitimately differ per request.</summary>
    private static async Task<string> ReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var members = json.RootElement.EnumerateObject()
            .Where(p => p.Name is not ("traceId" or "instance"))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"{p.Name}={p.Value.GetRawText()}");
        return string.Join('\n', members);
    }
}
