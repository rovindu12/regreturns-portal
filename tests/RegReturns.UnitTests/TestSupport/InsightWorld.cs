using RegReturns.Application.Insights;
using RegReturns.Domain.Identity;
using RegReturns.Domain.Institutions;
using RegReturns.Domain.Obligations;
using RegReturns.Domain.Periods;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;

namespace RegReturns.UnitTests.TestSupport;

/// <summary>
/// A return for insight tests: numeric, text, date and yes/no fields, a justified and an unjustified warning, approved
/// figures for the previous period and the same period last year, and bank text that must never reach a payload.
/// </summary>
internal sealed class InsightWorld
{
    public const string BankName = "Harbourline Test Bank PLC";
    public const string BankCode = "HTB";
    public const string Remarks = "Call jane.doe@harbourline.example about the spike";
    public const string Justification = "A large corporate borrower defaulted in February.";
    public const string MakerEmail = "maker@harbourline.example";

    public static readonly DateTimeOffset Now = new(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);

    public InsightWorld()
    {
        Bank = Institution.Create(BankCode, BankName, LicenceCategory.Commercial);
        ReturnType = ReturnType.Create("MDA", "Monthly Deposits and Advances Return", "Deposits and advances.", ReturnFrequency.Monthly, 15);

        Template = TemplateVersion.CreateDraft(ReturnType.Id, 1, new DateOnly(2026, 1, 1));
        Template.AddField("DEPOSITS", "Total deposits", "Balance sheet", FieldDataType.Amount, "VLD m").IsSuccess.ShouldBeTrue();
        Template.AddField("LOANS", "Gross loans", "Balance sheet", FieldDataType.Amount, "VLD m").IsSuccess.ShouldBeTrue();
        Template.AddField("NPL_RATIO", "Non-performing loan ratio", "Asset quality", FieldDataType.Percentage, "%").IsSuccess.ShouldBeTrue();
        Template.AddField("BRANCHES", "Branches", "Network", FieldDataType.WholeNumber, precision: 0).IsSuccess.ShouldBeTrue();
        Template.AddField("REMARKS", "Remarks", "Notes", FieldDataType.Text).IsSuccess.ShouldBeTrue();
        Template.AddField("AS_AT", "Figures as at", "Notes", FieldDataType.Date).IsSuccess.ShouldBeTrue();
        Template.AddField("AUDITED", "Audited", "Notes", FieldDataType.Boolean).IsSuccess.ShouldBeTrue();
        VarianceRule = ValidationRule.Variance(
            "NPL_VAR", "NPL_RATIO", Severity.Warning, 25m, VarianceBasis.PreviousPeriod, "Non-performing loan ratio moved by more than 25%.");
        RangeRule = ValidationRule.Range("NPL_MAX", "NPL_RATIO", Severity.Warning, null, 10m, "Non-performing loan ratio above 10%.");
        Template.AddRule(VarianceRule).IsSuccess.ShouldBeTrue();
        Template.AddRule(RangeRule).IsSuccess.ShouldBeTrue();
        Template.Publish().IsSuccess.ShouldBeTrue();

        Period = ReportingPeriod.Monthly(2026, 2);
        Obligation = ReturnObligation.Create(Bank.Id, ReturnType, Period);
        Maker = AppUser.Create("maker", "Mia Maker", MakerEmail, Bank.Id, [Role.BankMaker]).Value.ToActor();

        Submission = Submission.CreateDraft(Obligation, Template, Maker, SubmissionSource.Web, Now).Value;
        Submission.SetValues(
            Template,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["DEPOSITS"] = "1200.50",
                ["LOANS"] = "800",
                ["NPL_RATIO"] = "12.5",
                ["BRANCHES"] = "42",
                ["REMARKS"] = Remarks,
                ["AS_AT"] = "2026-02-28",
                ["AUDITED"] = "yes",
            },
            Maker,
            Now).IsSuccess.ShouldBeTrue();
        Submission.RecordValidation(
        [
            new FindingDraft(VarianceRule.Id, VarianceRule.Code, VarianceRule.TargetFieldCode, Severity.Warning, VarianceRule.Message),
            new FindingDraft(RangeRule.Id, RangeRule.Code, RangeRule.TargetFieldCode, Severity.Warning, RangeRule.Message),
        ]).IsSuccess.ShouldBeTrue();
        var variance = Submission.CurrentFindings.Single(f => f.RuleId == VarianceRule.Id);
        Submission.JustifyWarning(variance.Id, Justification, Maker, Now).IsSuccess.ShouldBeTrue();
    }

    public Institution Bank { get; }

    public ReturnType ReturnType { get; }

    public TemplateVersion Template { get; }

    public ValidationRule VarianceRule { get; }

    public ValidationRule RangeRule { get; }

    public ReportingPeriod Period { get; }

    public ReturnObligation Obligation { get; }

    public Actor Maker { get; }

    public Submission Submission { get; }

    /// <summary>Approved figures: a month earlier and a year earlier (no branches then).</summary>
    public static IReadOnlyDictionary<VarianceBasis, IReadOnlyDictionary<string, decimal>> Prior() =>
        new Dictionary<VarianceBasis, IReadOnlyDictionary<string, decimal>>
        {
            [VarianceBasis.PreviousPeriod] = new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["DEPOSITS"] = 1000.0000m,
                ["LOANS"] = 800m,
                ["NPL_RATIO"] = 5m,
            },
            [VarianceBasis.SamePeriodLastYear] = new Dictionary<string, decimal>(StringComparer.Ordinal)
            {
                ["DEPOSITS"] = 900m,
                ["NPL_RATIO"] = 4m,
            },
        };

    public InsightRequest Request() => InsightPayloadBuilder.Build(ReturnType, Template, Submission, Period, Prior());

    public string Payload() => InsightJson.Serialize(Request());

    /// <summary>A hand-made field for fact and narrator tests.</summary>
    public static InsightField Field(
        string code, decimal? current, decimal? previous = null, decimal? lastYear = null, string unit = "VLD m", string? label = null) =>
        new(
            code,
            label ?? code,
            "Section",
            unit,
            current,
            previous,
            lastYear,
            InsightPayloadBuilder.ChangePercent(current, previous),
            InsightPayloadBuilder.ChangePercent(current, lastYear));

    /// <summary>A hand-made payload for fact and narrator tests.</summary>
    public static InsightRequest RequestOf(IReadOnlyList<InsightField> fields, params InsightFinding[] findings) =>
        new(
            InsightRequest.CurrentSchema,
            new InsightReturnType("MDA", "Monthly Deposits and Advances Return"),
            ReturnFrequency.Monthly,
            "2026-02",
            "2026-01",
            "2025-02",
            1,
            fields,
            findings);
}
