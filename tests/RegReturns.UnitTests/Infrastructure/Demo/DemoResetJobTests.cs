using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

using RegReturns.Application.Demo;
using RegReturns.Application.Messaging;
using RegReturns.Domain.Common;
using RegReturns.Infrastructure.Demo;
using RegReturns.UnitTests.Auditing;

namespace RegReturns.UnitTests.Infrastructure.Demo;

public sealed class DemoResetJobTests
{
    private readonly ICommandHandler<ResetDemo, Result<DemoResetReport>> _handler =
        Substitute.For<ICommandHandler<ResetDemo, Result<DemoResetReport>>>();

    private readonly CapturingLogger<DemoResetJob> _logger = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 9, 2, 59, 0, TimeSpan.Zero));

    [Fact]
    public async Task Does_nothing_when_the_schedule_is_off()
    {
        using var job = Job(new NoDemoResetSchedule());

        await job.StartAsync(TestContext.Current.CancellationToken);
        await EventuallyAsync(() => _logger.Entries.Count > 0);

        _logger.Entries.ShouldHaveSingleItem().EventId.ShouldBe(5604);
        await _handler.DidNotReceiveWithAnyArgs().HandleAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Resets_as_scheduled_when_the_time_comes()
    {
        var called = new TaskCompletionSource<ResetDemo>(TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.HandleAsync(Arg.Any<ResetDemo>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                called.TrySetResult(call.Arg<ResetDemo>());
                return Result.Failure<DemoResetReport>(DemoErrors.InProgress);
            });
        using var job = Job(new CronDemoResetSchedule(Options.Create(new DemoOptions { Enabled = true, ResetSchedule = "0 3 * * *" })));

        await job.StartAsync(TestContext.Current.CancellationToken);
        await EventuallyAsync(() => _logger.Entries.Any(e => e.EventId == 5603));
        called.Task.IsCompleted.ShouldBeFalse();

        // The job runs on its own thread: move the clock on in small steps until it has waited out 03:00.
        await EventuallyAsync(() =>
        {
            _time.Advance(TimeSpan.FromSeconds(30));
            return called.Task.IsCompleted;
        });

        (await called.Task).ShouldBe(new ResetDemo(DemoResetTrigger.Scheduled));
        await job.StopAsync(TestContext.Current.CancellationToken);
        _logger.Entries.ShouldContain(e => e.EventId == 5603 && e.Message.Contains("2026-10-09 03:00:00Z", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failed_reset_is_logged_and_does_not_stop_the_host()
    {
        _handler.HandleAsync(Arg.Any<ResetDemo>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database unavailable"));
        using var job = Job(new NoDemoResetSchedule());

        await job.RunOnceAsync(TestContext.Current.CancellationToken);

        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.EventId.ShouldBe(5605);
        entry.Exception.ShouldBeOfType<InvalidOperationException>();
    }

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        var attempts = 0;
        while (!condition())
        {
            (++attempts).ShouldBeLessThan(100, "the job did not get there in time");
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    private DemoResetJob Job(IDemoResetSchedule schedule)
    {
        var services = new ServiceCollection().AddSingleton(_handler).BuildServiceProvider();
        return new DemoResetJob(services.GetRequiredService<IServiceScopeFactory>(), schedule, _time, _logger);
    }
}
