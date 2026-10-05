using RegReturns.Application.Returns;
using RegReturns.Domain.Submissions;

namespace RegReturns.UnitTests.Web;

public sealed class ReturnWorkflowStateTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(WorkflowAction.ReturnForCorrection)]
    [InlineData(WorkflowAction.Reject)]
    public void Latest_return_or_rejection_is_the_verdict_the_bank_reads_first(WorkflowAction action)
    {
        var state = State(Step(action, "Please correct the ratio."), Step(WorkflowAction.StartReview), Step(WorkflowAction.Submit));

        state.SupervisorVerdict.ShouldNotBeNull().Comment.ShouldBe("Please correct the ratio.");
    }

    [Fact]
    public void An_earlier_return_is_no_verdict_once_the_bank_resubmits()
    {
        var state = State(Step(WorkflowAction.Submit), Step(WorkflowAction.ReturnForCorrection, "Fix it."), Step(WorkflowAction.StartReview));

        state.SupervisorVerdict.ShouldBeNull();
    }

    [Fact]
    public void Allows_lists_only_the_offered_steps()
    {
        var state = State() with { Actions = [WorkflowAction.ReturnForCorrection] };

        state.Allows(WorkflowAction.ReturnForCorrection).ShouldBeTrue();
        state.Allows(WorkflowAction.Approve).ShouldBeFalse();
    }

    private static ReturnHistoryEntry Step(WorkflowAction action, string? comment = null) =>
        new(1, action, SubmissionStatus.UnderReview, SubmissionStatus.ReturnedForCorrection, "Someone", comment, At);

    private static ReturnWorkflowState State(params ReturnHistoryEntry[] newestFirst) =>
        new("HLB", "Harbourline Bank PLC", false, At, At, false, null, [], null, newestFirst);
}
