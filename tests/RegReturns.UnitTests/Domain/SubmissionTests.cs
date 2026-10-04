using RegReturns.Domain.Obligations;
using RegReturns.Domain.Submissions;
using RegReturns.UnitTests.TestSupport;

namespace RegReturns.UnitTests.Domain;

public sealed class SubmissionTests
{
    private readonly DomainFixture _f = new();

    [Fact]
    public void Maker_creates_a_draft_with_a_creation_event()
    {
        var result = Submission.CreateDraft(_f.Obligation, _f.Template, _f.Maker, SubmissionSource.Web, DomainFixture.Now);

        result.IsSuccess.ShouldBeTrue();
        var submission = result.Value;
        submission.Status.ShouldBe(SubmissionStatus.Draft);
        submission.Revision.ShouldBe(1);
        submission.InstitutionId.ShouldBe(_f.Bank.Id);
        submission.Events.Single().Action.ShouldBe(WorkflowAction.Create);
    }

    [Fact]
    public void Only_a_maker_of_the_obligated_bank_can_create_a_draft()
    {
        Submission.CreateDraft(_f.Obligation, _f.Template, _f.Checker, SubmissionSource.Web, DomainFixture.Now).Error
            .ShouldBe(SubmissionErrors.RoleRequired);
        Submission.CreateDraft(_f.Obligation, _f.Template, _f.Reviewer, SubmissionSource.Web, DomainFixture.Now).Error
            .ShouldBe(SubmissionErrors.RoleRequired);
    }

    [Fact]
    public void Setting_values_parses_numbers_with_invariant_culture_and_rejects_unknown_fields()
    {
        var submission = Submission.CreateDraft(_f.Obligation, _f.Template, _f.Maker, SubmissionSource.Web, DomainFixture.Now).Value;

        submission.SetValues(_f.Template, DomainFixture.ValidValues(assets: "1,234.50"), _f.Maker, DomainFixture.Now).IsSuccess.ShouldBeTrue();
        submission.FindValue("ASSETS")!.NumericValue.ShouldBe(1234.50m);

        var unknown = new Dictionary<string, string?>(StringComparer.Ordinal) { ["NOPE"] = "1" };
        submission.SetValues(_f.Template, unknown, _f.Maker, DomainFixture.Now).Error!.Code.ShouldBe(SubmissionErrors.UnknownField.Code);
    }

    [Fact]
    public void Full_happy_path_reaches_approved_and_fulfils_the_obligation()
    {
        var submission = _f.UnderReview();

        var result = submission.Approve(_f.Approver, _f.Obligation, "Approved.", DomainFixture.Now.AddDays(1));

        result.IsSuccess.ShouldBeTrue();
        submission.Status.ShouldBe(SubmissionStatus.Approved);
        submission.DecidedByUserId.ShouldBe(_f.Approver.UserId);
        _f.Obligation.Status.ShouldBe(ObligationStatus.Fulfilled);
        submission.Events.Select(e => e.Action).ShouldBe(
            [WorkflowAction.Create, WorkflowAction.Submit, WorkflowAction.StartReview, WorkflowAction.Approve]);
    }

    [Fact]
    public void Checker_cannot_submit_a_return_they_prepared()
    {
        var submission = _f.ValidatedDraft();
        var makerActingAsChecker = new RegReturns.Domain.Identity.Actor(
            _f.Maker.UserId, "Maker", [RegReturns.Domain.Identity.Role.BankChecker], _f.Bank.Id);

        submission.Submit(makerActingAsChecker, _f.Obligation, "Checked.", DomainFixture.Now).Error
            .ShouldBe(SubmissionErrors.CheckerIsMaker);
    }

    [Fact]
    public void Checker_from_another_bank_cannot_submit()
    {
        _f.ValidatedDraft().Submit(_f.OtherBankChecker, _f.Obligation, "Checked.", DomainFixture.Now).Error
            .ShouldBe(SubmissionErrors.WrongInstitution);
    }

    [Fact]
    public void Submission_requires_values_and_a_current_validation_run()
    {
        var empty = Submission.CreateDraft(_f.Obligation, _f.Template, _f.Maker, SubmissionSource.Web, DomainFixture.Now).Value;
        empty.Submit(_f.Checker, _f.Obligation, "Checked.", DomainFixture.Now).Error.ShouldBe(SubmissionErrors.NoValues);

        var stale = _f.ValidatedDraft();
        stale.SetValues(_f.Template, DomainFixture.ValidValues(assets: "999"), _f.Maker, DomainFixture.Now);
        stale.Submit(_f.Checker, _f.Obligation, "Checked.", DomainFixture.Now).Error.ShouldBe(SubmissionErrors.ValidationOutdated);
    }

    [Fact]
    public void Validation_errors_block_submission()
    {
        var submission = _f.ValidatedDraft();
        submission.RecordValidation([_f.ErrorFinding()]);

        submission.Submit(_f.Checker, _f.Obligation, "Checked.", DomainFixture.Now).Error.ShouldBe(SubmissionErrors.HasErrors);
    }

    [Fact]
    public void Warnings_allow_submission_only_once_justified()
    {
        var submission = _f.ValidatedDraft();
        submission.RecordValidation([_f.WarningFinding()]);
        submission.Submit(_f.Checker, _f.Obligation, "Checked.", DomainFixture.Now).Error.ShouldBe(SubmissionErrors.UnjustifiedWarnings);

        var warning = submission.CurrentFindings.Single();
        submission.JustifyWarning(warning.Id, "too short", _f.Checker, DomainFixture.Now).Error.ShouldBe(SubmissionErrors.JustificationLength);
        submission.JustifyWarning(warning.Id, "Seasonal dip confirmed with treasury.", _f.Checker, DomainFixture.Now).IsSuccess.ShouldBeTrue();

        submission.Submit(_f.Checker, _f.Obligation, "Checked.", DomainFixture.Now).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Justifications_carry_over_when_the_same_warning_reappears()
    {
        var submission = _f.ValidatedDraft();
        submission.RecordValidation([_f.WarningFinding()]);
        submission.JustifyWarning(submission.CurrentFindings.Single().Id, "Seasonal dip confirmed with treasury.", _f.Maker, DomainFixture.Now);

        submission.RecordValidation([_f.WarningFinding()]);

        submission.CurrentFindings.Single().Justification.ShouldBe("Seasonal dip confirmed with treasury.");
    }

    [Fact]
    public void Late_submission_is_flagged_against_the_due_date()
    {
        var afterDueDate = new DateTimeOffset(_f.Obligation.DueDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var submission = _f.Submitted(afterDueDate);

        submission.IsLate.ShouldBeTrue();
        _f.Obligation.IsLate.ShouldBeTrue();
        _f.Obligation.Status.ShouldBe(ObligationStatus.InProgress);
    }

    [Fact]
    public void Returning_for_correction_starts_a_new_revision_that_needs_revalidation()
    {
        var submission = _f.UnderReview();

        submission.ReturnForCorrection(_f.Reviewer, "Please fix the ratio.", DomainFixture.Now.AddHours(2)).IsSuccess.ShouldBeTrue();

        submission.Status.ShouldBe(SubmissionStatus.ReturnedForCorrection);
        submission.Revision.ShouldBe(2);
        submission.IsEditable.ShouldBeTrue();
        submission.Submit(_f.Checker, _f.Obligation, "Resubmitted.", DomainFixture.Now.AddHours(3)).Error
            .ShouldBe(SubmissionErrors.ValidationOutdated);

        submission.SetValues(_f.Template, DomainFixture.ValidValues(ratio: "16.00"), _f.SecondMaker, DomainFixture.Now.AddHours(3));
        submission.RecordValidation([]);
        submission.Submit(_f.Checker, _f.Obligation, "Resubmitted.", DomainFixture.Now.AddHours(4)).IsSuccess.ShouldBeTrue();
        submission.Status.ShouldBe(SubmissionStatus.Submitted);
    }

    [Fact]
    public void Approver_cannot_approve_a_return_they_reviewed()
    {
        var submission = _f.Submitted();
        var reviewerApprover = new RegReturns.Domain.Identity.Actor(
            _f.Approver.UserId,
            "Both",
            [RegReturns.Domain.Identity.Role.SupervisorReviewer, RegReturns.Domain.Identity.Role.SupervisorApprover],
            null);
        submission.StartReview(reviewerApprover, DomainFixture.Now).IsSuccess.ShouldBeTrue();

        submission.Approve(reviewerApprover, _f.Obligation, "Approved.", DomainFixture.Now).Error
            .ShouldBe(SubmissionErrors.ApproverIsReviewer);
        submission.Approve(_f.SecondApprover, _f.Obligation, "Approved.", DomainFixture.Now).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Bank_staff_cannot_perform_supervisory_steps()
    {
        var submission = _f.Submitted();

        submission.StartReview(_f.Checker, DomainFixture.Now).Error.ShouldBe(SubmissionErrors.RoleRequired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Return_and_reject_require_a_comment(string comment)
    {
        var submission = _f.UnderReview();

        submission.ReturnForCorrection(_f.Reviewer, comment, DomainFixture.Now).Error.ShouldBe(SubmissionErrors.CommentRequired);
        submission.Reject(_f.Approver, _f.Obligation, comment, DomainFixture.Now).Error.ShouldBe(SubmissionErrors.CommentRequired);
    }

    [Fact]
    public void Rejection_reopens_the_obligation()
    {
        var submission = _f.UnderReview();

        submission.Reject(_f.Approver, _f.Obligation, "Figures do not reconcile.", DomainFixture.Now).IsSuccess.ShouldBeTrue();

        submission.Status.ShouldBe(SubmissionStatus.Rejected);
        _f.Obligation.Status.ShouldBe(ObligationStatus.Open);
    }

    [Fact]
    public void Values_cannot_change_once_submitted()
    {
        var submission = _f.Submitted();

        submission.SetValues(_f.Template, DomainFixture.ValidValues(), _f.Maker, DomainFixture.Now).Error
            .ShouldBe(SubmissionErrors.NotEditable);
    }

    [Fact]
    public void Invalid_transitions_are_refused()
    {
        var submission = _f.ValidatedDraft();

        submission.StartReview(_f.Reviewer, DomainFixture.Now).Error.ShouldBe(SubmissionErrors.InvalidTransition);
        submission.Approve(_f.Approver, _f.Obligation, "Approved.", DomainFixture.Now).Error.ShouldBe(SubmissionErrors.InvalidTransition);
    }
}
