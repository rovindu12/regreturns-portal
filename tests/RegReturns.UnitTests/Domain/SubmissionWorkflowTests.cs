using RegReturns.Domain.Submissions;

namespace RegReturns.UnitTests.Domain;

public sealed class SubmissionWorkflowTests
{
    public static TheoryData<SubmissionStatus, WorkflowAction, SubmissionStatus?> Transitions => new()
    {
        { SubmissionStatus.Draft, WorkflowAction.Submit, SubmissionStatus.Submitted },
        { SubmissionStatus.ReturnedForCorrection, WorkflowAction.Submit, SubmissionStatus.Submitted },
        { SubmissionStatus.Submitted, WorkflowAction.StartReview, SubmissionStatus.UnderReview },
        { SubmissionStatus.UnderReview, WorkflowAction.ReturnForCorrection, SubmissionStatus.ReturnedForCorrection },
        { SubmissionStatus.UnderReview, WorkflowAction.Approve, SubmissionStatus.Approved },
        { SubmissionStatus.UnderReview, WorkflowAction.Reject, SubmissionStatus.Rejected },
        { SubmissionStatus.Draft, WorkflowAction.Approve, null },
        { SubmissionStatus.Submitted, WorkflowAction.Approve, null },
        { SubmissionStatus.Submitted, WorkflowAction.Submit, null },
        { SubmissionStatus.Approved, WorkflowAction.ReturnForCorrection, null },
        { SubmissionStatus.Rejected, WorkflowAction.Submit, null },
    };

    [Theory]
    [MemberData(nameof(Transitions))]
    public void Transition_table_matches_the_documented_workflow(
        SubmissionStatus from, WorkflowAction action, SubmissionStatus? expected)
    {
        SubmissionWorkflow.TargetOf(from, action).ShouldBe(expected);
    }

    [Fact]
    public void Final_states_allow_no_actions()
    {
        SubmissionWorkflow.ActionsFrom(SubmissionStatus.Approved).ShouldBeEmpty();
        SubmissionWorkflow.ActionsFrom(SubmissionStatus.Rejected).ShouldBeEmpty();
        SubmissionWorkflow.IsFinal(SubmissionStatus.Approved).ShouldBeTrue();
    }

    [Fact]
    public void Only_draft_and_returned_states_are_editable()
    {
        Enum.GetValues<SubmissionStatus>().Where(SubmissionWorkflow.IsEditable)
            .ShouldBe([SubmissionStatus.Draft, SubmissionStatus.ReturnedForCorrection]);
    }
}
