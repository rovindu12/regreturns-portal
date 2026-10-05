using System.Net;

using RegReturns.Infrastructure.Persistence.Seeding;
using RegReturns.IntegrationTests.Hosting;

namespace RegReturns.IntegrationTests.Api;

/// <summary>The per-client fixed-window rate limit (ADR 0027).</summary>
[Collection(HostedAppsDefinition.Name)]
public sealed class ApiRateLimitTests(RateLimitedApiFixture api) : IClassFixture<RateLimitedApiFixture>
{
    [Fact]
    public async Task A_client_over_its_limit_is_told_to_retry_later()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = api.Factory.ClientAs(await api.Database.RegisterClientAsync(DemoBank.Harbourline, active: true, ct));

        for (var i = 0; i < RateLimitedApiFixture.PermitLimit; i++)
        {
            (await client.GetAsync("/v1/me", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var refused = await client.GetAsync("/v1/me", ct);

        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        refused.Headers.RetryAfter!.Delta!.Value.TotalSeconds.ShouldBeInRange(1, 3600);
        (await refused.ReadProblemCodeAsync(ct)).ShouldBe("RateLimit.Exceeded");
    }

    [Fact]
    public async Task Each_client_has_its_own_limit()
    {
        var ct = TestContext.Current.CancellationToken;
        using var busy = api.Factory.ClientAs(await api.Database.RegisterClientAsync(DemoBank.LotusUnion, active: true, ct));
        using var quiet = api.Factory.ClientAs(await api.Database.RegisterClientAsync(DemoBank.LotusUnion, active: true, ct));
        for (var i = 0; i <= RateLimitedApiFixture.PermitLimit; i++)
        {
            await busy.GetAsync("/v1/me", ct);
        }

        var response = await quiet.GetAsync("/v1/me", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_endpoints_are_not_limited()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = api.Factory.CreateClient();

        for (var i = 0; i <= RateLimitedApiFixture.PermitLimit * 2; i++)
        {
            (await client.GetAsync("/health/live", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }
}
