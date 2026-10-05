using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

using RegReturns.Application.Authorization;
using RegReturns.Application.Identity;
using RegReturns.Domain.Auditing;
using RegReturns.Infrastructure.Persistence.Seeding;
using RegReturns.IntegrationTests.Hosting;
using RegReturns.ServiceDefaults.Web;

namespace RegReturns.IntegrationTests.Api;

/// <summary>
/// Access token validation through the API's real bearer scheme, its events and its policies, with tokens signed
/// by a local key published in a static OpenID Connect configuration.
/// </summary>
[Collection(HostedAppsDefinition.Name)]
public sealed class AccessTokenValidationTests(BearerApiFixture api) : IClassFixture<BearerApiFixture>
{
    private const string ProblemJson = "application/problem+json";

    [Fact]
    public async Task A_valid_access_token_for_a_registered_client_is_accepted()
    {
        var response = await CallMeAsync(await RegisteredClientAsync());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_token_that_expired_within_the_clock_skew_is_accepted()
    {
        var response = await CallMeAsync(await RegisteredClientAsync(), t => t.ExpiresIn = TimeSpan.FromSeconds(-20));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_token_that_expired_beyond_the_clock_skew_is_rejected()
    {
        var response = await CallMeAsync(await RegisteredClientAsync(), t => t.ExpiresIn = TimeSpan.FromSeconds(-45));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_for_another_audience_is_rejected()
    {
        var response = await CallMeAsync(await RegisteredClientAsync(), t => t.Audience = "https://api.elsewhere.example");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_from_another_issuer_is_rejected()
    {
        var response = await CallMeAsync(await RegisteredClientAsync(), t => t.Issuer = "https://iam.elsewhere.example/oauth2/token");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_id_token_style_jwt_carrying_the_api_audience_is_rejected()
    {
        var response = await CallMeAsync(await RegisteredClientAsync(), t => t.Type = JwtConstants.HeaderType);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_signed_with_hs256_is_rejected_even_with_a_known_key()
    {
        var response = await CallMeAsync(
            await RegisteredClientAsync(),
            t => t.SigningCredentials = new SigningCredentials(ApiTokens.TrustedSymmetricKey, SecurityAlgorithms.HmacSha256));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_signed_with_rs384_is_rejected_even_with_the_trusted_key()
    {
        var response = await CallMeAsync(
            await RegisteredClientAsync(),
            t => t.SigningCredentials = new SigningCredentials(ApiTokens.TrustedKey, SecurityAlgorithms.RsaSha384));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_signed_with_an_unknown_key_is_rejected()
    {
        var response = await CallMeAsync(
            await RegisteredClientAsync(),
            t => t.SigningCredentials = new SigningCredentials(ApiTokens.UntrustedKey, SecurityAlgorithms.RsaSha256));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_without_a_client_id_is_rejected()
    {
        var response = await CallMeAsync(await RegisteredClientAsync(), t => t.IncludeClientId = false);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Anonymous_requests_get_a_bearer_challenge_and_a_problem_with_the_trace_id()
    {
        using var client = api.Factory.CreateClient();

        var response = await client.GetAsync("/v1/me", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ShouldContain(h => h.Scheme == "Bearer");
        response.Content.Headers.ContentType!.MediaType.ShouldBe(ProblemJson);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        json.RootElement.GetProperty("status").GetInt32().ShouldBe(401);
        json.RootElement.GetProperty("traceId").GetString().ShouldBe(TraceId(response));
    }

    [Fact]
    public async Task A_rejected_token_is_audited_by_failure_type_without_token_details()
    {
        var ct = TestContext.Current.CancellationToken;
        const string otherAudience = "https://api.elsewhere.example";

        // Anonymous failures are de-duplicated per address each minute, and every test request comes from the same
        // (empty) address, so forget earlier failures from this class first.
        ((MemoryCache)api.Factory.Services.GetRequiredService<IMemoryCache>()).Clear();
        var path = $"/v1/institutions/{Guid.NewGuid():N}";
        using var client = ClientWith(ApiTokens.Create(await RegisteredClientAsync(), t => t.Audience = otherAudience));

        var response = await client.GetAsync(path, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var entry = (await api.Database.AuditEntriesAsync(TraceId(response), ct)).ShouldHaveSingleItem();
        entry.Action.ShouldBe(AuditAction.AuthenticationFailed);
        entry.ActorType.ShouldBe(ActorType.Anonymous);
        entry.Details.ShouldBe($"path={path}; reason={nameof(SecurityTokenInvalidAudienceException)}");
    }

    [Fact]
    public async Task A_token_without_the_required_scope_is_forbidden_and_audited_with_the_policy()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientId = await RegisteredClientAsync();

        var response = await CallMeAsync(clientId, t => t.Scope = ApiScopes.ReturnsRead);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.ShouldBe(ProblemJson);
        var entry = (await api.Database.AuditEntriesAsync(TraceId(response), ct)).ShouldHaveSingleItem();
        entry.Action.ShouldBe(AuditAction.AccessDenied);
        entry.ActorType.ShouldBe(ActorType.ApiClient);
        entry.ActorSubjectId.ShouldBe(clientId);
        entry.InstitutionCode.ShouldBe(DemoBank.Harbourline);
        entry.Details.ShouldBe($"path=/v1/me; reason={Policies.ApiReferenceRead}");
    }

    private Task<string> RegisteredClientAsync() =>
        api.Database.RegisterClientAsync(DemoBank.Harbourline, active: true, TestContext.Current.CancellationToken);

    private async Task<HttpResponseMessage> CallMeAsync(string clientId, Action<AccessTokenSpec>? customise = null)
    {
        using var client = ClientWith(ApiTokens.Create(clientId, customise));
        return await client.GetAsync("/v1/me", TestContext.Current.CancellationToken);
    }

    private HttpClient ClientWith(string token)
    {
        var client = api.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string TraceId(HttpResponseMessage response) =>
        response.Headers.GetValues(WebDefaultsExtensions.TraceIdHeader).Single();
}
