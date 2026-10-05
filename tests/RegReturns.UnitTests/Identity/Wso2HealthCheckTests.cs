using System.Collections.Concurrent;
using System.Net;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using NSubstitute;

using RegReturns.Infrastructure;
using RegReturns.Infrastructure.Identity.Wso2;

namespace RegReturns.UnitTests.Identity;

public sealed class Wso2HealthCheckTests
{
    private const string Discovery = "https://iam.valoria.test/oauth2/token/.well-known/openid-configuration";
    private const string Jwks = "https://iam.valoria.test/oauth2/jwks";

    private static readonly Wso2Options Settings = new() { Authority = new Uri("https://iam.valoria.test/") };

    [Fact]
    public async Task Reachable_discovery_and_keys_are_healthy()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await CheckAsync(handler, TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Check_fetches_the_discovery_document_and_the_signing_keys()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await CheckAsync(handler, TestContext.Current.CancellationToken);

        handler.Requests.ShouldBe([Discovery, Jwks]);
    }

    [Fact]
    public async Task Failed_discovery_is_unhealthy_with_both_status_codes()
    {
        var handler = new StubHandler(request => new HttpResponseMessage(
            request.RequestUri!.AbsoluteUri == Discovery ? HttpStatusCode.NotFound : HttpStatusCode.OK));

        var result = await CheckAsync(handler, TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description.ShouldBe("WSO2 answered discovery with 404 and JWKS with 200.");
    }

    [Fact]
    public async Task Failed_signing_keys_are_unhealthy()
    {
        var handler = new StubHandler(request => new HttpResponseMessage(
            request.RequestUri!.AbsoluteUri == Jwks ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));

        var result = await CheckAsync(handler, TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description.ShouldBe("WSO2 answered discovery with 200 and JWKS with 503.");
    }

    [Fact]
    public async Task Unreachable_server_is_unhealthy_with_the_cause()
    {
        var failure = new HttpRequestException("The SSL connection could not be established.");
        var handler = new StubHandler(_ => throw failure);

        var result = await CheckAsync(handler, TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description.ShouldBe("WSO2 is not reachable over the back channel.");
        result.Exception.ShouldBeSameAs(failure);
    }

    [Fact]
    public async Task Timeout_is_unhealthy()
    {
        var handler = new StubHandler(_ => throw new TaskCanceledException("timed out"));

        var result = await CheckAsync(handler, TestContext.Current.CancellationToken);

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description.ShouldBe("WSO2 did not answer in time.");
    }

    [Fact]
    public async Task Cancellation_by_the_caller_is_not_reported_as_unhealthy()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => CheckAsync(handler, cancelled.Token));
    }

    [Fact]
    public async Task Registered_check_reaches_wso2_through_the_backchannel_rewrite()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Wso2:Authority"] = "https://iam.valoria.test/",
                ["Wso2:BackchannelAuthority"] = "https://wso2:9443/",
            })
            .Build();
        var services = new ServiceCollection().AddLogging().AddWso2Backchannel(configuration);
        services.AddHttpClient(Wso2Backchannel.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider();

        var report = await provider.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(r => r.Name == "wso2", TestContext.Current.CancellationToken);

        report.Status.ShouldBe(HealthStatus.Healthy);
        report.Entries["wso2"].Tags.ShouldContain(DependencyInjection.ReadyTag);
        handler.Requests.ShouldBe(
        [
            "https://wso2:9443/oauth2/token/.well-known/openid-configuration",
            "https://wso2:9443/oauth2/jwks",
        ]);
    }

    private static async Task<HealthCheckResult> CheckAsync(StubHandler handler, CancellationToken cancellationToken)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Wso2Backchannel.HttpClientName).Returns(_ => new HttpClient(handler, disposeHandler: false));
        var check = new Wso2HealthCheck(factory, Options.Create(Settings));
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("wso2", check, HealthStatus.Unhealthy, tags: null),
        };

        return await check.CheckHealthAsync(context, cancellationToken);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private readonly ConcurrentQueue<string> _requests = new();

        public IReadOnlyList<string> Requests => [.. _requests];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _requests.Enqueue(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(respond(request));
        }
    }
}
