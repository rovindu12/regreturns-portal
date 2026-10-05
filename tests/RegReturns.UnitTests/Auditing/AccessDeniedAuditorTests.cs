using System.Security.Claims;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using RegReturns.Application.Auditing;
using RegReturns.Application.Identity;
using RegReturns.Domain.Auditing;
using RegReturns.Infrastructure.Auditing;

namespace RegReturns.UnitTests.Auditing;

public sealed class AccessDeniedAuditorTests : IDisposable
{
    private const string Path = "/submissions/42";
    private const string Policy = "Bank.SubmitReturn";
    private const string Ip = "10.1.2.3";
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    private readonly IAuditTrail _auditTrail = Substitute.For<IAuditTrail>();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 4, 12, 0, 10, TimeSpan.Zero));
    private readonly CapturingLogger<AccessDeniedAuditor> _logger = new();
    private readonly AccessDeniedAuditor _auditor;

    public AccessDeniedAuditorTests()
    {
        _auditTrail.RecordAsync(Arg.Any<AuditRecord>(), Arg.Any<CancellationToken>()).Returns(1L);
        _auditor = new AccessDeniedAuditor(_auditTrail, _cache, _time, _logger);
    }

    [Fact]
    public async Task First_denial_is_recorded_with_the_actor_path_and_reason()
    {
        var recorded = await RecordAsync(User("user-1"));

        recorded.ShouldBeTrue();
        await _auditTrail.Received(1).RecordAsync(
            new AuditRecord(
                AuditAction.AccessDenied,
                ActorType.User,
                "user-1",
                "Nadia Fernhill",
                "ALPHA",
                Details: $"path={Path}; reason={Policy}",
                IpAddress: Ip,
                CorrelationId: TraceId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Denial_without_a_reason_records_only_the_path()
    {
        await _auditor.RecordAsync(User("user-1"), AuditAction.AccessDenied, Path, "  ", Ip, TraceId, TestContext.Current.CancellationToken);

        await _auditTrail.Received(1).RecordAsync(
            Arg.Is<AuditRecord>(r => r.Details == $"path={Path}"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Repeated_denial_in_the_same_minute_is_recorded_once()
    {
        await RecordAsync(User("user-1"));
        _time.Advance(TimeSpan.FromSeconds(40));

        var second = await RecordAsync(User("user-1"));

        second.ShouldBeFalse();
        await _auditTrail.Received(1).RecordAsync(Arg.Any<AuditRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Same_denial_in_the_next_minute_is_recorded_again()
    {
        await RecordAsync(User("user-1"));
        _time.Advance(TimeSpan.FromSeconds(50));

        var nextMinute = await RecordAsync(User("user-1"));

        nextMinute.ShouldBeTrue();
        await _auditTrail.Received(2).RecordAsync(Arg.Any<AuditRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Denial_on_another_path_is_recorded_separately()
    {
        await RecordAsync(User("user-1"));

        var otherPath = await _auditor.RecordAsync(
            User("user-1"), AuditAction.AccessDenied, "/admin", Policy, Ip, TraceId, TestContext.Current.CancellationToken);

        otherPath.ShouldBeTrue();
    }

    [Fact]
    public async Task Denial_of_another_actor_is_recorded_separately()
    {
        await RecordAsync(User("user-1"));

        (await RecordAsync(User("user-2"))).ShouldBeTrue();
    }

    [Fact]
    public async Task Authentication_failure_is_recorded_separately_from_access_denied()
    {
        await RecordAsync(User("user-1"));

        var failed = await _auditor.RecordAsync(
            User("user-1"), AuditAction.AuthenticationFailed, Path, "invalid_token", Ip, TraceId, TestContext.Current.CancellationToken);

        failed.ShouldBeTrue();
        await _auditTrail.Received(1).RecordAsync(
            Arg.Is<AuditRecord>(r => r.Action == AuditAction.AuthenticationFailed), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Anonymous_callers_from_different_addresses_are_recorded_separately()
    {
        await RecordAsync(null, "10.0.0.1");

        var other = await RecordAsync(null, "10.0.0.2");

        other.ShouldBeTrue();
        await _auditTrail.Received(1).RecordAsync(
            Arg.Is<AuditRecord>(r => r.ActorType == ActorType.Anonymous && r.ActorSubjectId == AuditActor.AnonymousSubject && r.IpAddress == "10.0.0.2"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Anonymous_caller_retrying_from_one_address_is_recorded_once()
    {
        await RecordAsync(null, "10.0.0.1");

        (await RecordAsync(null, "10.0.0.1")).ShouldBeFalse();
    }

    [Fact]
    public async Task Anonymous_caller_probing_many_paths_from_one_address_is_recorded_once_a_minute()
    {
        await RecordAsync(null, "10.0.0.1");

        var probe = await _auditor.RecordAsync(
            null, AuditAction.AuthenticationFailed, "/v1/random-1", "invalid_token", "10.0.0.1", TraceId, TestContext.Current.CancellationToken);
        var another = await _auditor.RecordAsync(
            null, AuditAction.AuthenticationFailed, "/v1/random-2", "invalid_token", "10.0.0.1", TraceId, TestContext.Current.CancellationToken);

        probe.ShouldBeTrue();
        another.ShouldBeFalse();
    }

    [Fact]
    public async Task Audit_store_failure_is_logged_and_not_thrown()
    {
        _auditTrail.RecordAsync(Arg.Any<AuditRecord>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database unavailable"));

        var recorded = await RecordAsync(User("user-1"));

        recorded.ShouldBeFalse();
        _logger.Entries.ShouldContain(e => e.Level == LogLevel.Error && e.EventId == 3002 && e.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task Request_cancelled_before_the_write_is_not_swallowed()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            _auditor.RecordAsync(User("user-1"), AuditAction.AccessDenied, Path, Policy, Ip, TraceId, cancelled.Token));
        await _auditTrail.DidNotReceive().RecordAsync(Arg.Any<AuditRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Write_does_not_use_the_request_token()
    {
        using var request = new CancellationTokenSource();

        await _auditor.RecordAsync(User("user-1"), AuditAction.AccessDenied, Path, Policy, Ip, TraceId, request.Token);

        await _auditTrail.Received(1).RecordAsync(Arg.Any<AuditRecord>(), Arg.Is<CancellationToken>(t => t != request.Token));
    }

    [Fact]
    public async Task Failed_write_is_retried_by_the_next_denial_in_the_same_minute()
    {
        _auditTrail.RecordAsync(Arg.Any<AuditRecord>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<long>(new OperationCanceledException()), Task.FromResult(2L));

        (await RecordAsync(User("user-1"))).ShouldBeFalse();

        (await RecordAsync(User("user-1"))).ShouldBeTrue();
    }

    [Fact]
    public async Task Signed_in_caller_probing_many_ids_on_one_endpoint_is_recorded_once_a_minute()
    {
        var first = await _auditor.RecordAsync(
            User("user-1"), AuditAction.AccessDenied, "/v1/institutions/AAA", "v1/institutions/{code}", Policy, Ip, TraceId,
            TestContext.Current.CancellationToken);
        var second = await _auditor.RecordAsync(
            User("user-1"), AuditAction.AccessDenied, "/v1/institutions/BBB", "v1/institutions/{code}", Policy, Ip, TraceId,
            TestContext.Current.CancellationToken);

        first.ShouldBeTrue();
        second.ShouldBeFalse();
    }

    [Fact]
    public async Task Recorded_denial_is_also_logged_as_a_warning_without_personal_data()
    {
        await RecordAsync(User("user-1"));

        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.EventId.ShouldBe(3001);
        entry.Message.ShouldNotContain("user-1");
        entry.Message.ShouldNotContain("Nadia");
    }

    [Theory]
    [InlineData(AuditAction.SignIn)]
    [InlineData(AuditAction.SignOut)]
    public async Task Other_actions_are_refused(AuditAction action)
    {
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            _auditor.RecordAsync(User("user-1"), action, Path, Policy, Ip, TraceId, TestContext.Current.CancellationToken));
        await _auditTrail.DidNotReceive().RecordAsync(Arg.Any<AuditRecord>(), Arg.Any<CancellationToken>());
    }

    public void Dispose() => _cache.Dispose();

    private Task<bool> RecordAsync(ClaimsPrincipal? principal, string ip = Ip) =>
        _auditor.RecordAsync(principal, AuditAction.AccessDenied, Path, Policy, ip, TraceId, TestContext.Current.CancellationToken);

    private static ClaimsPrincipal User(string subject) => new(new ClaimsIdentity(
        [
            new Claim(ClaimNames.Subject, subject),
            new Claim(ClaimNames.Name, "Nadia Fernhill"),
            new Claim(ClaimNames.InstitutionId, "ALPHA"),
        ],
        authenticationType: "Test"));
}
