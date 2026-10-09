using Microsoft.Extensions.Options;

using NSubstitute;

using RegReturns.Application.Auditing;
using RegReturns.Application.Demo;
using RegReturns.Application.Identity;
using RegReturns.Domain.Auditing;
using RegReturns.Domain.Common;
using RegReturns.Domain.Identity;
using RegReturns.UnitTests.Auditing;

namespace RegReturns.UnitTests.Application.Demo;

public sealed class ResetDemoHandlerTests
{
    private static readonly AuditOrigin AdminOrigin =
        new(new AuditActor(ActorType.User, "wso2-admin", "Demo Administrator", null), "203.0.113.7", "trace-1");

    private static readonly DemoResetReport Report = new(
        DemoResetTrigger.Manual,
        new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero),
        850,
        77,
        [new DemoTableCount("returns.Submissions", 120)],
        new DemoSeedCounts(3, 3, 140, 118, 0, 0));

    private readonly IDemoReset _reset = Substitute.For<IDemoReset>();
    private readonly ICurrentActor _currentActor = Substitute.For<ICurrentActor>();
    private readonly IAuditContext _auditContext = Substitute.For<IAuditContext>();
    private readonly CapturingLogger<ResetDemoHandler> _logger = new();
    private readonly DemoOptions _options = new() { Enabled = true, ResetCooldownMinutes = 15 };

    public ResetDemoHandlerTests()
    {
        _auditContext.Current.Returns(AdminOrigin);
        _reset.ResetAsync(Arg.Any<DemoResetRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(Report));
    }

    [Fact]
    public async Task Nothing_is_reset_outside_demo_mode()
    {
        _options.Enabled = false;
        SignedInAs(Role.SystemAdmin);

        var result = await HandleAsync(DemoResetTrigger.Manual);

        result.Error.ShouldBe(DemoErrors.Disabled);
        await _reset.DidNotReceiveWithAnyArgs().ResetAsync(default!, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(Role.SupervisorApprover)]
    [InlineData(Role.Auditor)]
    public async Task Only_a_system_administrator_can_press_the_button(Role role)
    {
        SignedInAs(role);

        var result = await HandleAsync(DemoResetTrigger.Manual);

        result.Error.ShouldBe(DemoErrors.AdminsOnly);
        await _reset.DidNotReceiveWithAnyArgs().ResetAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_caller_without_a_user_record_is_refused()
    {
        _currentActor.GetAsync(Arg.Any<CancellationToken>()).Returns(Result.Failure<Actor>(ActorErrors.NotLinked));

        (await HandleAsync(DemoResetTrigger.Manual)).Error.ShouldBe(DemoErrors.AdminsOnly);
    }

    [Fact]
    public async Task An_administrator_resets_with_the_cooldown_as_themselves()
    {
        SignedInAs(Role.SystemAdmin);

        var result = await HandleAsync(DemoResetTrigger.Manual);

        result.Value.ShouldBe(Report);
        await _reset.Received(1).ResetAsync(
            new DemoResetRequest(DemoResetTrigger.Manual, AdminOrigin, TimeSpan.FromMinutes(15)), Arg.Any<CancellationToken>());
        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.EventId.ShouldBe(5601);
        entry.Message.ShouldContain("audit entry 77");
    }

    [Fact]
    public async Task A_scheduled_reset_asked_for_from_a_request_is_refused()
    {
        // Only work with no caller behind it is the system, so nobody can skip the cooldown by asking for "scheduled".
        var result = await HandleAsync(DemoResetTrigger.Scheduled);

        result.Error.ShouldBe(DemoErrors.AdminsOnly);
        await _reset.DidNotReceiveWithAnyArgs().ResetAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_scheduled_reset_acts_as_the_system_without_a_cooldown()
    {
        _auditContext.Current.Returns(AuditOrigin.System);

        var result = await HandleAsync(DemoResetTrigger.Scheduled);

        result.IsSuccess.ShouldBeTrue();
        await _reset.Received(1).ResetAsync(
            new DemoResetRequest(DemoResetTrigger.Scheduled, AuditOrigin.System, null), Arg.Any<CancellationToken>());
        await _currentActor.DidNotReceiveWithAnyArgs().GetAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_refusal_from_the_reset_is_returned_and_logged_with_its_code()
    {
        SignedInAs(Role.SystemAdmin);
        _reset.ResetAsync(Arg.Any<DemoResetRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<DemoResetReport>(DemoErrors.InProgress));

        var result = await HandleAsync(DemoResetTrigger.Manual);

        result.Error.ShouldBe(DemoErrors.InProgress);
        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.EventId.ShouldBe(5602);
        entry.Message.ShouldContain(DemoErrors.InProgress.Code);
    }

    private void SignedInAs(Role role) =>
        _currentActor.GetAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Success(new Actor(Guid.CreateVersion7(), "Someone", [role], null)));

    private Task<Result<DemoResetReport>> HandleAsync(DemoResetTrigger trigger) =>
        new ResetDemoHandler(_reset, _currentActor, _auditContext, Options.Create(_options), _logger)
            .HandleAsync(new ResetDemo(trigger), TestContext.Current.CancellationToken);
}
