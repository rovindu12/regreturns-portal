using RegReturns.Domain.Submissions;

namespace RegReturns.UnitTests.Domain;

public sealed class SubmissionWorkflowTests
{
    /// <summary>The documented workflow (CLAUDE.md, plan §3): every allowed step and where it leads.</summary>
    private static readonly Dictionary<(SubmissionStatus From, WorkflowAction Action), SubmissionStatus> Documented = new()
    {
        [(SubmissionStatus.Draft, WorkflowAction.Submit)] = SubmissionStatus.Submitted,
        [(SubmissionStatus.ReturnedForCorrection, WorkflowAction.Submit)] = SubmissionStatus.Submitted,
        [(SubmissionStatus.Submitted, WorkflowAction.StartReview)] = SubmissionStatus.UnderReview,
        [(SubmissionStatus.UnderReview, WorkflowAction.ReturnForCorrection)] = SubmissionStatus.ReturnedForCorrection,
        [(SubmissionStatus.UnderReview, WorkflowAction.Approve)] = SubmissionStatus.Approved,
        [(SubmissionStatus.UnderReview, WorkflowAction.Reject)] = SubmissionStatus.Rejected,
    };

    /// <summary>Every state paired with every action: the full transition matrix.</summary>
    public static TheoryData<SubmissionStatus, WorkflowAction> EveryStateAndAction()
    {
        var data = new TheoryData<SubmissionStatus, WorkflowAction>();
        foreach (var status in Enum.GetValues<SubmissionStatus>())
        {
            foreach (var action in Enum.GetValues<WorkflowAction>())
            {
                data.Add(status, action);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryStateAndAction))]
    public void Every_state_and_action_pair_follows_the_documented_workflow(SubmissionStatus from, WorkflowAction action)
    {
        SubmissionStatus? expected = Documented.TryGetValue((from, action), out var to) ? to : null;

        SubmissionWorkflow.TargetOf(from, action).ShouldBe(expected);
    }

    [Fact]
    public void Creating_a_draft_is_never_a_transition()
    {
        Enum.GetValues<SubmissionStatus>().ShouldAllBe(s => SubmissionWorkflow.TargetOf(s, WorkflowAction.Create) == null);
    }

    [Fact]
    public void Final_states_allow_no_actions()
    {
        SubmissionWorkflow.ActionsFrom(SubmissionStatus.Approved).ShouldBeEmpty();
        SubmissionWorkflow.ActionsFrom(SubmissionStatus.Rejected).ShouldBeEmpty();
        SubmissionWorkflow.IsFinal(SubmissionStatus.Approved).ShouldBeTrue();
    }

    [Fact]
    public void Actions_from_a_state_are_listed_in_workflow_order()
    {
        SubmissionWorkflow.ActionsFrom(SubmissionStatus.UnderReview)
            .ShouldBe([WorkflowAction.ReturnForCorrection, WorkflowAction.Approve, WorkflowAction.Reject]);
    }

    [Fact]
    public void Only_draft_and_returned_states_are_editable()
    {
        Enum.GetValues<SubmissionStatus>().Where(SubmissionWorkflow.IsEditable)
            .ShouldBe([SubmissionStatus.Draft, SubmissionStatus.ReturnedForCorrection]);
    }
}
