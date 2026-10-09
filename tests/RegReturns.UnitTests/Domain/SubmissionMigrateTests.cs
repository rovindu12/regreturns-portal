using RegReturns.Domain.Common;
using RegReturns.Domain.Identity;
using RegReturns.Domain.Obligations;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;
using RegReturns.UnitTests.TestSupport;

namespace RegReturns.UnitTests.Domain;

public sealed class SubmissionMigrateTests
{
    private const string Comment = "Migrated from VRRS (VRRS_MLR_EXPORT.csv, line 7) by migration run 42.";
    private const string WarningNote = "Accepted in the legacy system, which recorded no justification.";

    // The fixture's obligation is February 2026: it ends on 28 February and is due on 15 March. Now is 10 March.
    private static readonly DateTimeOffset Filed = new(2026, 3, 5, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Approved = new(2026, 3, 9, 14, 0, 0, TimeSpan.Zero);

    private readonly DomainFixture _f = new();
    private readonly AppUser _account = AppUser.ForMigration();

    private Actor Migrator => _account.ToActor();

    [Fact]
    public void Migrate_files_an_approved_return_for_the_obligation()
    {
        var submission = Migrate().Value;

        submission.Status.ShouldBe(SubmissionStatus.Approved);
        submission.Source.ShouldBe(SubmissionSource.Migration);
        submission.Revision.ShouldBe(1);
        submission.ObligationId.ShouldBe(_f.Obligation.Id);
        submission.InstitutionId.ShouldBe(_f.Bank.Id);
        submission.ReturnTypeId.ShouldBe(_f.ReturnType.Id);
        submission.TemplateVersionId.ShouldBe(_f.Template.Id);
        submission.IsEditable.ShouldBeFalse();
    }

    [Fact]
    public void Every_user_of_a_migrated_return_is_the_migration_account()
    {
        var submission = Migrate().Value;

        submission.PreparedByUserId.ShouldBe(_account.Id);
        submission.LastEditedByUserId.ShouldBe(_account.Id);
        submission.SubmittedByUserId.ShouldBe(_account.Id);
        submission.DecidedByUserId.ShouldBe(_account.Id);
        submission.ReviewedByUserId.ShouldBeNull();
    }

    [Fact]
    public void A_migrated_return_keeps_the_legacy_filing_and_approval_dates()
    {
        var submission = Migrate().Value;

        submission.CreatedAt.ShouldBe(Filed);
        submission.LastEditedAt.ShouldBe(Filed);
        submission.FirstSubmittedAt.ShouldBe(Filed);
        submission.LastSubmittedAt.ShouldBe(Filed);
        submission.DecidedAt.ShouldBe(Approved);
    }

    [Fact]
    public void A_migrated_return_counts_as_validated_once()
    {
        var submission = Migrate().Value;

        submission.EditVersion.ShouldBe(1);
        submission.ValidatedEditVersion.ShouldBe(1);
    }

    [Fact]
    public void A_migrated_return_keeps_the_values_it_was_given()
    {
        var submission = Migrate(values: DomainFixture.ValidValues(assets: "1234.5", ratio: "12.25")).Value;

        submission.Values.Count.ShouldBe(2);
        submission.FindValue("ASSETS")!.NumericValue.ShouldBe(1234.5m);
        submission.FindValue("RATIO")!.NumericValue.ShouldBe(12.25m);
    }

    [Fact]
    public void A_migrated_return_filed_by_its_due_date_is_on_time()
    {
        var lastMinute = new DateTimeOffset(2026, 3, 15, 23, 59, 0, TimeSpan.Zero);

        var submission = Migrate(Filing(filed: lastMinute, approved: lastMinute), now: lastMinute.AddDays(1)).Value;

        submission.IsLate.ShouldBeFalse();
        _f.Obligation.IsLate.ShouldBeFalse();
    }

    [Fact]
    public void A_migrated_return_filed_after_its_due_date_is_late()
    {
        var dayAfterDue = new DateTimeOffset(2026, 3, 16, 8, 0, 0, TimeSpan.Zero);

        var submission = Migrate(Filing(filed: dayAfterDue, approved: dayAfterDue.AddDays(2)), now: dayAfterDue.AddDays(3)).Value;

        submission.IsLate.ShouldBeTrue();
        _f.Obligation.IsLate.ShouldBeTrue();
    }

    [Fact]
    public void Migrating_marks_the_obligation_submitted_at_the_legacy_filing_date_and_fulfilled()
    {
        Migrate().IsSuccess.ShouldBeTrue();

        _f.Obligation.Status.ShouldBe(ObligationStatus.Fulfilled);
        _f.Obligation.FirstSubmittedAt.ShouldBe(Filed);
    }

    [Fact]
    public void A_migrated_return_has_one_migrate_step_carrying_the_comment()
    {
        var submission = Migrate(Filing(comment: "  " + Comment + "  ")).Value;

        var step = submission.Events.ShouldHaveSingleItem();
        step.Action.ShouldBe(WorkflowAction.Migrate);
        step.Revision.ShouldBe(1);
        step.FromStatus.ShouldBeNull();
        step.ToStatus.ShouldBe(SubmissionStatus.Approved);
        step.ActorUserId.ShouldBe(_account.Id);
        step.ActorDisplayName.ShouldBe("Legacy data migration");
        step.Comment.ShouldBe(Comment);
        step.OccurredAt.ShouldBe(DomainFixture.Now);
    }

    [Fact]
    public void Warnings_of_a_migrated_return_are_justified_with_the_note()
    {
        var submission = Migrate(Filing(warningNote: "  " + WarningNote + " "), findings: [_f.WarningFinding()]).Value;

        var warning = submission.CurrentFindings.ShouldHaveSingleItem();
        warning.Severity.ShouldBe(Severity.Warning);
        warning.Revision.ShouldBe(1);
        warning.Justification.ShouldBe(WarningNote);
        warning.JustifiedByUserId.ShouldBe(_account.Id);
        warning.JustifiedAt.ShouldBe(DomainFixture.Now);
        warning.BlocksSubmission.ShouldBeFalse();
    }

    [Fact]
    public void A_migrated_return_offers_no_further_workflow_step()
    {
        var submission = Migrate().Value;

        submission.ActionsFor(_f.Approver).ShouldBeEmpty();
        submission.Permits(WorkflowAction.Approve, _f.Approver).Error.ShouldBe(SubmissionErrors.InvalidTransition);
    }

    [Fact]
    public void The_template_must_be_for_the_obligations_return_type()
    {
        var otherType = TemplateVersion.CreateDraft(Guid.CreateVersion7(), 1, new DateOnly(2026, 1, 1));

        Migrate(template: otherType).Error.ShouldBe(SubmissionErrors.TemplateMismatch);
    }

    [Fact]
    public void Bank_staff_cannot_migrate_a_return()
    {
        Migrate(migrator: _f.Maker).Error.ShouldBe(SubmissionErrors.RegulatorOnly);
    }

    [Fact]
    public void An_obligation_with_a_return_in_the_portal_cannot_be_migrated()
    {
        _f.Submitted();

        Migrate().Error.ShouldBe(SubmissionErrors.AlreadyFiled);
    }

    [Fact]
    public void An_obligation_reopened_after_a_rejected_portal_return_cannot_be_migrated()
    {
        var submission = _f.UnderReview();
        submission.Reject(_f.Approver, _f.Obligation, "Figures do not reconcile.", DomainFixture.Now).IsSuccess.ShouldBeTrue();
        _f.Obligation.Status.ShouldBe(ObligationStatus.Open);

        Migrate().Error.ShouldBe(SubmissionErrors.AlreadyFiled);
    }

    [Fact]
    public void The_same_obligation_cannot_be_migrated_twice()
    {
        Migrate().IsSuccess.ShouldBeTrue();

        Migrate().Error.ShouldBe(SubmissionErrors.AlreadyFiled);
    }

    [Theory]
    [InlineData("2026-02-27T23:59:00Z", "2026-03-09T00:00:00Z")]
    [InlineData("2026-03-05T09:30:00Z", "2026-03-05T09:29:59Z")]
    [InlineData("2026-03-05T09:30:00Z", "2026-03-10T09:00:01Z")]
    public void Legacy_dates_out_of_order_or_in_the_future_are_refused(string filed, string approved)
    {
        var filing = Filing(filed: Parse(filed), approved: Parse(approved));

        Migrate(filing).Error.ShouldBe(SubmissionErrors.MigrationDates);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void The_migration_step_needs_a_comment(string comment)
    {
        Migrate(Filing(comment: comment)).Error.ShouldBe(SubmissionErrors.CommentRequired);
    }

    [Fact]
    public void A_comment_longer_than_a_workflow_comment_can_be_is_refused()
    {
        var result = Migrate(Filing(comment: new string('c', WorkflowEvent.CommentMaxLength + 1)));

        result.Error!.Code.ShouldBe(SubmissionErrors.CommentRequired.Code);
        result.Error.Message.ShouldContain(WorkflowEvent.CommentMaxLength.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("Legacy, no reason.")]
    [InlineData("   Too short anyway.   ")]
    public void A_warning_note_shorter_than_a_justification_is_refused(string note)
    {
        Migrate(Filing(warningNote: note), findings: [_f.WarningFinding()]).Error.ShouldBe(SubmissionErrors.JustificationLength);
    }

    [Fact]
    public void A_warning_note_longer_than_a_justification_can_be_is_refused()
    {
        var note = new string('n', ValidationFinding.JustificationMaxLength + 1);

        Migrate(Filing(warningNote: note)).Error.ShouldBe(SubmissionErrors.JustificationLength);
    }

    [Fact]
    public void Values_that_break_an_error_rule_are_refused()
    {
        Migrate(findings: [_f.WarningFinding(), _f.ErrorFinding()]).Error.ShouldBe(SubmissionErrors.HasErrors);
    }

    [Fact]
    public void A_return_without_values_is_refused()
    {
        Migrate(values: new Dictionary<string, string?>(StringComparer.Ordinal)).Error.ShouldBe(SubmissionErrors.NoValues);
    }

    [Fact]
    public void A_value_for_a_field_outside_the_template_is_refused()
    {
        var values = DomainFixture.ValidValues();
        values["NOPE"] = "1";

        Migrate(values: values).Error!.Code.ShouldBe(SubmissionErrors.UnknownField.Code);
    }

    [Fact]
    public void A_value_longer_than_a_value_can_be_is_refused()
    {
        var values = DomainFixture.ValidValues(assets: new string('9', SubmissionValue.RawValueMaxLength + 1));

        Migrate(values: values).Error!.Code.ShouldBe(SubmissionErrors.ValueTooLong.Code);
    }

    [Fact]
    public void A_refused_migration_leaves_the_obligation_open()
    {
        Migrate(values: new Dictionary<string, string?>(StringComparer.Ordinal)).IsFailure.ShouldBeTrue();

        _f.Obligation.Status.ShouldBe(ObligationStatus.Open);
        _f.Obligation.FirstSubmittedAt.ShouldBeNull();
    }

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private static MigratedFiling Filing(
        DateTimeOffset? filed = null, DateTimeOffset? approved = null, string comment = Comment, string warningNote = WarningNote) =>
        new(filed ?? Filed, approved ?? Approved, comment, warningNote);

    private Result<Submission> Migrate(
        MigratedFiling? filing = null,
        IEnumerable<FindingDraft>? findings = null,
        IReadOnlyDictionary<string, string?>? values = null,
        Actor? migrator = null,
        TemplateVersion? template = null,
        DateTimeOffset? now = null) =>
        Submission.Migrate(
            _f.Obligation,
            template ?? _f.Template,
            values ?? DomainFixture.ValidValues(),
            findings ?? [],
            filing ?? Filing(),
            migrator ?? Migrator,
            now ?? DomainFixture.Now);
}
