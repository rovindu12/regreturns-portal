using System.Net;
using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;

using NSubstitute;

using RegReturns.Api.Authentication;
using RegReturns.Application.Auditing;
using RegReturns.Application.Authorization;
using RegReturns.Application.Identity;
using RegReturns.Domain.Auditing;
using RegReturns.Infrastructure.Auditing;

namespace RegReturns.UnitTests.Api;

public sealed class ApiJwtBearerEventsTests : IDisposable
{
    private const string ClientId = "regreturns-bank-hlb";
    private static readonly AuthenticationScheme Scheme = new(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));

    private readonly IAuditTrail _trail = Substitute.For<IAuditTrail>();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly ApiJwtBearerEvents _events;

    public ApiJwtBearerEventsTests()
    {
        var auditor = new AccessDeniedAuditor(
            _trail, _cache, new FakeTimeProvider(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero)), NullLogger<AccessDeniedAuditor>.Instance);
        _events = new ApiJwtBearerEvents(auditor, NullLogger<ApiJwtBearerEvents>.Instance);
    }

    [Fact]
    public async Task A_token_naming_its_client_passes()
    {
        var context = Validated((ClaimNames.AuthorizedParty, ClientId), (ClaimNames.ClientId, ClientId));

        await _events.TokenValidated(context);

        context.Result.ShouldBeNull();
    }

    [Fact]
    public async Task A_token_without_a_client_id_fails()
    {
        var context = Validated((ClaimNames.Subject, ClientId));

        await _events.TokenValidated(context);

        context.Result!.Failure.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_token_without_a_client_id_is_audited_as_an_authentication_failure()
    {
        await _events.TokenValidated(Validated((ClaimNames.Subject, ClientId)));

        await _trail.Received(1).RecordAsync(
            Arg.Is<AuditRecord>(r => r.Action == AuditAction.AuthenticationFailed
                && r.Details == $"path=/v1/me; reason={ApiJwtBearerEvents.MissingClientIdReason}"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_token_whose_azp_and_client_id_differ_fails()
    {
        var context = Validated((ClaimNames.AuthorizedParty, ClientId), (ClaimNames.ClientId, "regreturns-bank-ccb"));

        await _events.TokenValidated(context);

        context.Result!.Failure.ShouldNotBeNull();
    }

    [Fact]
    public async Task Authentication_failures_are_audited_with_the_exception_type_only()
    {
        var context = new AuthenticationFailedContext(Http(), Scheme, new JwtBearerOptions())
        {
            Exception = new SecurityTokenExpiredException("IDX10223: Lifetime validation failed for eyJhbGciOi.secret"),
        };

        await _events.AuthenticationFailed(context);

        await _trail.Received(1).RecordAsync(
            Arg.Is<AuditRecord>(r => r.Action == AuditAction.AuthenticationFailed
                && r.ActorType == ActorType.Anonymous
                && r.Details == $"path=/v1/me; reason={nameof(SecurityTokenExpiredException)}"
                && r.IpAddress == "203.0.113.7"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Audited_paths_never_include_the_query_string()
    {
        var http = Http();
        http.Request.QueryString = new QueryString("?access_token=eyJhbGciOi");

        await _events.AuthenticationFailed(new AuthenticationFailedContext(http, Scheme, new JwtBearerOptions())
        {
            Exception = new SecurityTokenInvalidSignatureException(),
        });

        await _trail.Received(1).RecordAsync(
            Arg.Is<AuditRecord>(r => r.Details != null && !r.Details.Contains("access_token", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Forbidden_requests_are_audited_with_the_endpoint_policy()
    {
        var http = Http();
        http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new AuthorizeAttribute(Policies.ApiReferenceRead)), "test"));
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimNames.AuthorizedUserType, ClaimNames.ApplicationTokenType), new Claim(ClaimNames.ClientId, ClientId)], "Bearer"));

        await _events.Forbidden(new ForbiddenContext(http, Scheme, new JwtBearerOptions()));

        await _trail.Received(1).RecordAsync(
            Arg.Is<AuditRecord>(r => r.Action == AuditAction.AccessDenied
                && r.ActorType == ActorType.ApiClient
                && r.ActorSubjectId == ClientId
                && r.Details == $"path=/v1/me; reason={Policies.ApiReferenceRead}"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Aggregate_failures_name_each_exception_type_once()
    {
        var failure = new AggregateException(
            new SecurityTokenExpiredException(), new SecurityTokenExpiredException(), new SecurityTokenInvalidAudienceException());

        ApiJwtBearerEvents.DescribeFailure(failure)
            .ShouldBe($"{nameof(SecurityTokenExpiredException)},{nameof(SecurityTokenInvalidAudienceException)}");
    }

    public void Dispose() => _cache.Dispose();

    private static DefaultHttpContext Http()
    {
        var http = new DefaultHttpContext();
        http.Request.Path = "/v1/me";
        http.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        return http;
    }

    private static TokenValidatedContext Validated(params (string Type, string Value)[] claims) =>
        new(Http(), Scheme, new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "Bearer")),
        };
}
