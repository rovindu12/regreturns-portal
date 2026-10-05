using System.Security.Claims;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;

using RegReturns.Application.Auditing;
using RegReturns.Application.Identity;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Auditing;
using RegReturns.Domain.Common;
using RegReturns.Domain.Identity;
using RegReturns.Infrastructure.Auditing;
using RegReturns.Web.Identity;

namespace RegReturns.UnitTests.Web;

public sealed class SignInProcessorTests : IDisposable
{
    private const string Subject = "8d0c7b3e-0000-4000-8000-000000000001";
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    private static readonly RequestOrigin Origin = new("/signin-oidc", "203.0.113.7", TraceId);

    private readonly ICommandHandler<LinkSignedInUser, Result<SignedInUserLink>> _linkHandler =
        Substitute.For<ICommandHandler<LinkSignedInUser, Result<SignedInUserLink>>>();

    private readonly IAuditTrail _auditTrail = Substitute.For<IAuditTrail>();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly SignInProcessor _processor;

    public SignInProcessorTests()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));
        var failureAuditor = new AccessDeniedAuditor(_auditTrail, _cache, clock, NullLogger<AccessDeniedAuditor>.Instance);
        _processor = new SignInProcessor(_linkHandler, _auditTrail, failureAuditor, NullLogger<SignInProcessor>.Instance);
    }

    [Fact]
    public async Task Successful_sign_in_links_the_user_from_the_normalised_claims()
    {
        LinkSucceeds();

        await _processor.ProcessAsync(TokenPrincipal(), Origin, TestContext.Current.CancellationToken);

        await _linkHandler.Received(1).HandleAsync(
            Arg.Is<LinkSignedInUser>(c =>
                c.SubjectId == Subject && c.UserName == "maker" && c.DisplayName == "Maker (Harbourline Bank PLC)" &&
                c.Email == "maker@harbourline.example" && c.InstitutionCode == "HBL" && c.Roles.SequenceEqual(new[] { Role.BankMaker })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Successful_sign_in_records_SignIn_with_the_ip_address_and_trace_id()
    {
        LinkSucceeds();

        await _processor.ProcessAsync(TokenPrincipal(), Origin, TestContext.Current.CancellationToken);

        await _auditTrail.Received(1).RecordAsync(
            Arg.Is<AuditRecord>(r =>
                r.Action == AuditAction.SignIn && r.ActorType == ActorType.User && r.ActorSubjectId == Subject &&
                r.InstitutionCode == "HBL" && r.IpAddress == "203.0.113.7" && r.CorrelationId == TraceId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Successful_sign_in_returns_the_normalised_principal()
    {
        LinkSucceeds();

        var result = await _processor.ProcessAsync(TokenPrincipal(), Origin, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.FindFirst("aud").ShouldBeNull();
        result.Value.FindFirst(ClaimNames.Roles)!.Value.ShouldBe(RoleNames.BankMaker);
    }

    [Fact]
    public async Task Failed_link_refuses_the_sign_in_and_records_AuthenticationFailed_with_the_error_code()
    {
        _linkHandler.HandleAsync(Arg.Any<LinkSignedInUser>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<SignedInUserLink>(LinkSignedInUserHandler.UnknownInstitution));

        var result = await _processor.ProcessAsync(TokenPrincipal(), Origin, TestContext.Current.CancellationToken);

        result.Error.ShouldBe(LinkSignedInUserHandler.UnknownInstitution);
        await _auditTrail.Received(1).RecordAsync(
            Arg.Is<AuditRecord>(r =>
                r.Action == AuditAction.AuthenticationFailed && r.ActorSubjectId == Subject &&
                r.Details!.Contains("reason=User.UnknownInstitution", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
        await _auditTrail.DidNotReceive().RecordAsync(Arg.Is<AuditRecord>(r => r.Action == AuditAction.SignIn), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Token_without_a_user_name_is_refused_without_linking()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimNames.Subject, Subject)], "oidc"));

        var result = await _processor.ProcessAsync(principal, Origin, TestContext.Current.CancellationToken);

        result.Error.ShouldBe(SignInErrors.UserNameMissing);
        await _linkHandler.DidNotReceive().HandleAsync(Arg.Any<LinkSignedInUser>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Token_without_a_session_id_is_refused_without_linking()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            TokenPrincipal().Claims.Where(c => c.Type != ClaimNames.SessionId), "oidc"));

        var result = await _processor.ProcessAsync(principal, Origin, TestContext.Current.CancellationToken);

        result.Error.ShouldBe(SignInErrors.SessionMissing);
        await _linkHandler.DidNotReceive().HandleAsync(Arg.Any<LinkSignedInUser>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cookie_carries_the_institution_code_as_RegReturns_stores_it()
    {
        _linkHandler.HandleAsync(Arg.Any<LinkSignedInUser>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SignedInUserLink(Guid.CreateVersion7(), "Maker (Harbourline Bank PLC)", "HBL")));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            TokenPrincipal().Claims.Select(c => c.Type == ClaimNames.InstitutionId ? new Claim(c.Type, "hbl") : c), "oidc"));

        var result = await _processor.ProcessAsync(principal, Origin, TestContext.Current.CancellationToken);

        result.Value.FindAll(ClaimNames.InstitutionId).Select(c => c.Value).ShouldBe(["HBL"]);
    }

    public void Dispose() => _cache.Dispose();

    private void LinkSucceeds() =>
        _linkHandler.HandleAsync(Arg.Any<LinkSignedInUser>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new SignedInUserLink(Guid.CreateVersion7(), "Maker (Harbourline Bank PLC)")));

    private static ClaimsPrincipal TokenPrincipal() => new(new ClaimsIdentity(
    [
        new Claim(ClaimNames.Subject, Subject),
        new Claim(ClaimNames.UserName, "maker"),
        new Claim(ClaimNames.Name, "Maker (Harbourline Bank PLC)"),
        new Claim(ClaimNames.Email, "maker@harbourline.example"),
        new Claim(ClaimNames.InstitutionId, "HBL"),
        new Claim(ClaimNames.Roles, RoleNames.BankMaker),
        new Claim(ClaimNames.AuthenticationMethods, "BasicAuthenticator"),
        new Claim(ClaimNames.SessionId, "wso2-session-1"),
        new Claim("aud", "regreturns-portal"),
    ], "oidc"));
}
