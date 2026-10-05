using RegReturns.Domain.Identity;
using RegReturns.Domain.Submissions;
using RegReturns.UnitTests.TestSupport;

namespace RegReturns.UnitTests.Domain;

public sealed class SubmissionPermissionTests
{
    private readonly DomainFixture _f = new();

    /// <summary>Who may do what in each state; every pair not listed allows nothing.</summary>
    public static TheoryData<SubmissionStatus, string, WorkflowAction[]> Matrix => new()
    {
        { SubmissionStatus.Draft, nameof(DomainFixture.Checker), [WorkflowAction.Submit] },
        { SubmissionStatus.Draft, nameof(DomainFixture.Maker), [] },
        { SubmissionStatus.Draft, nameof(DomainFixture.OtherBankChecker), [] },
        { SubmissionStatus.Draft, nameof(DomainFixture.Reviewer), [] },
        { SubmissionStatus.Submitted, nameof(DomainFixture.Reviewer), [WorkflowAction.StartReview] },
        { SubmissionStatus.Submitted, nameof(DomainFixture.Approver), [] },
        { SubmissionStatus.Submitted, nameof(DomainFixture.Checker), [] },
        { SubmissionStatus.UnderReview, nameof(DomainFixture.Reviewer), [WorkflowAction.ReturnForCorrection] },
        {
            SubmissionStatus.UnderReview, nameof(DomainFixture.Approver),
            [WorkflowAction.ReturnForCorrection, WorkflowAction.Approve, WorkflowAction.Reject]
        },
        { SubmissionStatus.UnderReview, nameof(DomainFixture.Checker), [] },
        { SubmissionStatus.ReturnedForCorrection, nameof(DomainFixture.Checker), [WorkflowAction.Submit] },
        { SubmissionStatus.ReturnedForCorrection, nameof(DomainFixture.Reviewer), [] },
        { SubmissionStatus.Approved, nameof(DomainFixture.Approver), [] },
        { SubmissionStatus.Approved, nameof(DomainFixture.Checker), [] },
        { SubmissionStatus.Rejected, nameof(DomainFixture.Checker), [] },
        { SubmissionStatus.Rejected, nameof(DomainFixture.Reviewer), [] },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void Actions_offered_depend_on_state_role_and_organisation(SubmissionStatus status, string actorName, WorkflowAction[] expected)
    {
        var submission = InState(status);

        submission.ActionsFor(ActorNamed(actorName)).ShouldBe(expected);
    }

    [Fact]
    public void Checker_who_last_edited_the_values_is_not_offered_submit()
    {
        var submission = _f.ValidatedDraft();
        var editingChecker = new Actor(Guid.CreateVersion7(), "Edits and checks", [Role.BankMaker, Role.BankChecker], _f.Bank.Id);
        submission.SetValues(_f.Template, DomainFixture.ValidValues(ratio: "16.00"), editingChecker, DomainFixture.Now).IsSuccess.ShouldBeTrue();

        submission.Permits(WorkflowAction.Submit, editingChecker).Error.ShouldBe(SubmissionErrors.CheckerIsMaker);
        submission.ActionsFor(editingChecker).ShouldBeEmpty();
        submission.ActionsFor(_f.Checker).ShouldBe([WorkflowAction.Submit]);
    }

    [Fact]
    public void Approver_who_reviewed_the_return_may_only_send_it_back()
    {
        var submission = _f.Submitted();
        var both = new Actor(Guid.CreateVersion7(), "Reviews and approves", [Role.SupervisorReviewer, Role.SupervisorApprover], null);
        submission.StartReview(both, DomainFixture.Now).IsSuccess.ShouldBeTrue();

        submission.ActionsFor(both).ShouldBe([WorkflowAction.ReturnForCorrection]);
        submission.Permits(WorkflowAction.Approve, both).Error.ShouldBe(SubmissionErrors.ApproverIsReviewer);
        submission.ActionsFor(_f.Approver).ShouldContain(WorkflowAction.Approve);
    }

    [Fact]
    public void Permits_reports_the_state_before_the_role()
    {
        var submission = _f.ValidatedDraft();

        submission.Permits(WorkflowAction.Approve, _f.Checker).Error.ShouldBe(SubmissionErrors.InvalidTransition);
        submission.Permits(WorkflowAction.Submit, _f.OtherBankChecker).Error.ShouldBe(SubmissionErrors.WrongInstitution);
        submission.Permits(WorkflowAction.Submit, _f.Reviewer).Error.ShouldBe(SubmissionErrors.RoleRequired);
    }

    [Fact]
    public void Bank_staff_holding_a_supervisor_role_are_still_refused_supervisory_steps()
    {
        var submission = _f.Submitted();
        var bankReviewer = new Actor(Guid.CreateVersion7(), "Bank reviewer", [Role.SupervisorReviewer], _f.Bank.Id);

        submission.Permits(WorkflowAction.StartReview, bankReviewer).Error.ShouldBe(SubmissionErrors.RegulatorOnly);
    }

    private Actor ActorNamed(string name) => name switch
    {
        nameof(DomainFixture.Maker) => _f.Maker,
        nameof(DomainFixture.Checker) => _f.Checker,
        nameof(DomainFixture.OtherBankChecker) => _f.OtherBankChecker,
        nameof(DomainFixture.Reviewer) => _f.Reviewer,
        nameof(DomainFixture.Approver) => _f.Approver,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown actor."),
    };

    private Submission InState(SubmissionStatus status)
    {
        switch (status)
        {
            case SubmissionStatus.Draft:
                return _f.ValidatedDraft();
            case SubmissionStatus.Submitted:
                return _f.Submitted();
            case SubmissionStatus.UnderReview:
                return _f.UnderReview();
            case SubmissionStatus.ReturnedForCorrection:
                var returned = _f.UnderReview();
                returned.ReturnForCorrection(_f.Reviewer, "Please check the ratio.", DomainFixture.Now).IsSuccess.ShouldBeTrue();
                return returned;
            case SubmissionStatus.Approved:
                var approved = _f.UnderReview();
                approved.Approve(_f.Approver, _f.Obligation, "Approved.", DomainFixture.Now).IsSuccess.ShouldBeTrue();
                return approved;
            case SubmissionStatus.Rejected:
                var rejected = _f.UnderReview();
                rejected.Reject(_f.Approver, _f.Obligation, "Does not reconcile.", DomainFixture.Now).IsSuccess.ShouldBeTrue();
                return rejected;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown status.");
        }
    }
}
