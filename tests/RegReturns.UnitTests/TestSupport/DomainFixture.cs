using RegReturns.Domain.Identity;
using RegReturns.Domain.Institutions;
using RegReturns.Domain.Obligations;
using RegReturns.Domain.Periods;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;

namespace RegReturns.UnitTests.TestSupport;

/// <summary>
/// A small, self-contained world for domain tests: one bank, one return type with a two-field template,
/// and one user per role.
/// </summary>
internal sealed class DomainFixture
{
    public static readonly DateTimeOffset Now = new(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);

    public DomainFixture()
    {
        Bank = Institution.Create("TST", "Test Bank PLC", LicenceCategory.Commercial);
        OtherBank = Institution.Create("OTH", "Other Bank PLC", LicenceCategory.Commercial);
        ReturnType = ReturnType.Create("TRT", "Test Return", "A return used in tests.", ReturnFrequency.Monthly, 15);

        Template = TemplateVersion.CreateDraft(ReturnType.Id, 1, new DateOnly(2026, 1, 1));
        Template.AddField("ASSETS", "Total assets", "Balance sheet", FieldDataType.Amount, "VLD m").IsSuccess.ShouldBeTrue();
        Template.AddField("RATIO", "Ratio", "Ratios", FieldDataType.Percentage, "%").IsSuccess.ShouldBeTrue();
        WarningRule = ValidationRule.Range("RATIO_MIN", "RATIO", Severity.Warning, 10m, null, "Ratio below 10%.");
        ErrorRule = ValidationRule.Required("ASSETS_REQ", "ASSETS", "Total assets is required.");
        Template.AddRule(WarningRule).IsSuccess.ShouldBeTrue();
        Template.AddRule(ErrorRule).IsSuccess.ShouldBeTrue();
        Template.Publish().IsSuccess.ShouldBeTrue();

        Obligation = ReturnObligation.Create(Bank.Id, ReturnType, ReportingPeriod.Monthly(2026, 2));

        Maker = User("maker", Role.BankMaker, Bank.Id);
        SecondMaker = User("maker2", Role.BankMaker, Bank.Id);
        Checker = User("checker", Role.BankChecker, Bank.Id);
        OtherBankChecker = User("other.checker", Role.BankChecker, OtherBank.Id);
        Reviewer = User("reviewer", Role.SupervisorReviewer, null);
        Approver = User("approver", Role.SupervisorApprover, null);
        SecondApprover = User("approver2", Role.SupervisorApprover, null);
    }

    public Institution Bank { get; }

    public Institution OtherBank { get; }

    public ReturnType ReturnType { get; }

    public TemplateVersion Template { get; }

    public ValidationRule WarningRule { get; }

    public ValidationRule ErrorRule { get; }

    public ReturnObligation Obligation { get; }

    public Actor Maker { get; }

    public Actor SecondMaker { get; }

    public Actor Checker { get; }

    public Actor OtherBankChecker { get; }

    public Actor Reviewer { get; }

    public Actor Approver { get; }

    public Actor SecondApprover { get; }

    public static Dictionary<string, string?> ValidValues(string assets = "1000.00", string ratio = "15.00") =>
        new(StringComparer.Ordinal) { ["ASSETS"] = assets, ["RATIO"] = ratio };

    public FindingDraft WarningFinding() =>
        new(WarningRule.Id, WarningRule.Code, WarningRule.TargetFieldCode, Severity.Warning, WarningRule.Message);

    public FindingDraft ErrorFinding() =>
        new(ErrorRule.Id, ErrorRule.Code, ErrorRule.TargetFieldCode, Severity.Error, ErrorRule.Message);

    /// <summary>A draft with valid values, validated with no findings.</summary>
    public Submission ValidatedDraft(DateTimeOffset? at = null)
    {
        var now = at ?? Now;
        var submission = Submission.CreateDraft(Obligation, Template, Maker, SubmissionSource.Web, now).Value;
        submission.SetValues(Template, ValidValues(), Maker, now).IsSuccess.ShouldBeTrue();
        submission.RecordValidation([]).IsSuccess.ShouldBeTrue();
        return submission;
    }

    public Submission Submitted(DateTimeOffset? at = null)
    {
        var submission = ValidatedDraft(at);
        submission.Submit(Checker, Obligation, "Checked.", at ?? Now).IsSuccess.ShouldBeTrue();
        return submission;
    }

    public Submission UnderReview()
    {
        var submission = Submitted();
        submission.StartReview(Reviewer, Now.AddHours(1)).IsSuccess.ShouldBeTrue();
        return submission;
    }

    private static Actor User(string userName, Role role, Guid? institutionId) =>
        AppUser.Create(userName, userName, $"{userName}@test.example", institutionId, [role]).Value.ToActor();
}
