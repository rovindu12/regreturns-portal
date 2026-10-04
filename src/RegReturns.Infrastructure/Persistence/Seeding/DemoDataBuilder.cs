using System.Globalization;

using RegReturns.Domain.Common;
using RegReturns.Domain.Identity;
using RegReturns.Domain.Institutions;
using RegReturns.Domain.Obligations;
using RegReturns.Domain.Periods;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;

namespace RegReturns.Infrastructure.Persistence.Seeding;

/// <summary>
/// Builds the demo data set: five banks, three return types, twelve months of filing history and a set of
/// in-flight returns, with deliberate anomalies for the dashboards, validation and AI insight demos.
/// Every submission goes through the real domain workflow, so seeded data obeys the same rules as live data.
/// </summary>
/// <param name="now">The time the data set is anchored to; history covers the twelve months before it.</param>
internal sealed class DemoDataBuilder(DateTimeOffset now)
{
    /// <summary>Number of monthly periods of history, including the current in-flight month.</summary>
    public const int MonthsOfHistory = 12;

    /// <summary>Number of quarterly periods of history, including the current in-flight quarter.</summary>
    public const int QuartersOfHistory = 4;

    private readonly Dictionary<(string Bank, string Return, ReportingPeriod Period), IReadOnlyDictionary<string, decimal>> _approved = [];
    private readonly List<ReturnObligation> _obligations = [];
    private readonly List<Submission> _submissions = [];
    private DemoUsers _users = null!;

    /// <summary>Builds the data set.</summary>
    public DemoDataSet Build()
    {
        var institutions = DemoBank.All.ToDictionary(b => b.Code, b => Institution.Create(b.Code, b.Name, b.Category));
        _users = new DemoUsers(institutions);

        var mlr = MlrTemplate.CreateReturnType();
        var mda = MdaTemplate.CreateReturnType();
        var qcar = QcarTemplate.CreateReturnType();

        var latestMonth = ReportingPeriod.Containing(ReturnFrequency.Monthly, Today).Previous();
        var latestQuarter = ReportingPeriod.Containing(ReturnFrequency.Quarterly, Today).Previous();
        var months = PeriodsEndingAt(latestMonth, MonthsOfHistory);
        var quarters = PeriodsEndingAt(latestQuarter, QuartersOfHistory);

        var effectiveFrom = quarters[0].Start < months[0].Start ? quarters[0].Start : months[0].Start;
        var templates = new Dictionary<string, TemplateVersion>(StringComparer.Ordinal)
        {
            [MlrTemplate.Code] = MlrTemplate.CreateTemplate(mlr, effectiveFrom),
            [MdaTemplate.Code] = MdaTemplate.CreateTemplate(mda, effectiveFrom),
            [QcarTemplate.Code] = QcarTemplate.CreateTemplate(qcar, effectiveFrom),
        };

        foreach (var bank in DemoBank.All)
        {
            var institution = institutions[bank.Code];
            AddReturns(bank, institution, mlr, templates[MlrTemplate.Code], months, (b, p, h) => FigureGenerator.Mlr(b, p, MlrShapeFor(b, h)));
            AddReturns(bank, institution, mda, templates[MdaTemplate.Code], months, (b, p, h) => FigureGenerator.Mda(b, p, NplRatioFor(b, h)));
            AddReturns(bank, institution, qcar, templates[QcarTemplate.Code], quarters, (b, p, _) => FigureGenerator.Qcar(b, p));
        }

        return new DemoDataSet(
            institutions.Values.ToList(),
            _users.All.ToList(),
            [mlr, mda, qcar],
            templates.Values.ToList(),
            _obligations,
            _submissions);
    }

    private DateOnly Today => DateOnly.FromDateTime(now.UtcDateTime);

    private static List<ReportingPeriod> PeriodsEndingAt(ReportingPeriod latest, int count)
    {
        var periods = new List<ReportingPeriod> { latest };
        while (periods.Count < count)
        {
            periods.Insert(0, periods[0].Previous());
        }

        return periods;
    }

    private static FigureGenerator.MlrShape MlrShapeFor(DemoBank bank, int monthsAgo) => (bank.Code, monthsAgo) switch
    {
        // Lotus Union: large deposit outflow drains HQLA and pushes LCR below 100%.
        (DemoBank.LotusUnion, DemoScenario.LotusLiquidityShockMonthsAgo) => new(Level1Factor: 0.40m, Level2AFactor: 0.50m),

        // Crestmont: revision 1 counts listed equities as Level 2B above the 15% cap; the reviewer sends it back.
        (DemoBank.Crestmont, DemoScenario.CrestmontReturnedMonthsAgo) => new(Level2BShare: 0.06m),
        _ => FigureGenerator.MlrShape.Normal,
    };

    private static decimal? NplRatioFor(DemoBank bank, int monthsAgo) => (bank.Code, monthsAgo) switch
    {
        // Harbourline: two large manufacturing exposures reclassified as non-performing, then partial recovery.
        (DemoBank.Harbourline, DemoScenario.HarbourlineNplJumpMonthsAgo) => 8.9m,
        (DemoBank.Harbourline, DemoScenario.HarbourlineNplJumpMonthsAgo - 1) => 7.9m,
        (DemoBank.Harbourline, DemoScenario.HarbourlineNplJumpMonthsAgo - 2) => 7.6m,
        _ => null,
    };

    private static void Ensure(Result result, string context)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Seed workflow failed ({context}): {result.Error!.Code} {result.Error.Message}");
        }
    }

    private static Dictionary<string, string?> AsInput(IReadOnlyDictionary<string, decimal> values) =>
        values.ToDictionary(kv => kv.Key, kv => (string?)kv.Value.ToString("0.00", CultureInfo.InvariantCulture), StringComparer.Ordinal);

    private void AddReturns(
        DemoBank bank,
        Institution institution,
        ReturnType returnType,
        TemplateVersion template,
        List<ReportingPeriod> periods,
        Func<DemoBank, ReportingPeriod, int, Dictionary<string, decimal>> figures)
    {
        for (var i = 0; i < periods.Count; i++)
        {
            var period = periods[i];
            var periodsAgo = periods.Count - 1 - i;
            var obligation = ReturnObligation.Create(institution.Id, returnType, period);
            _obligations.Add(obligation);

            var plan = DemoScenario.PlanFor(bank.Code, returnType.Code, periodsAgo);
            if (plan.Status == DemoScenario.Outcome.NotFiled)
            {
                continue;
            }

            var context = $"{bank.Code} {returnType.Code} {period.Label}";
            var values = figures(bank, period, periodsAgo);
            var clock = plan.InFlight ? InFlightClock(period) : HistoryClock(obligation, plan);
            var submission = CreateAndFill(bank, obligation, template, values, clock.Created, context);
            _submissions.Add(submission);

            if (plan.Status == DemoScenario.Outcome.Draft)
            {
                continue;
            }

            if (plan.ReturnedOnce)
            {
                RunReturnedForCorrection(bank, returnType, template, submission, obligation, figures(bank, period, -1), period, periodsAgo, clock);
                values = figures(bank, period, -1);
            }
            else
            {
                ValidateAndSubmit(bank, returnType, template, submission, obligation, values, period, periodsAgo, clock.Submitted, context);
            }

            if (plan.Status == DemoScenario.Outcome.Submitted)
            {
                continue;
            }

            Ensure(submission.StartReview(_users.Actor(DemoUsers.Reviewer), clock.ReviewStarted), context);
            if (plan.Status == DemoScenario.Outcome.UnderReview)
            {
                continue;
            }

            Ensure(submission.Approve(_users.Actor(DemoUsers.Approver), obligation, DemoScenario.ApprovalComment, clock.Decided), context);
            _approved[(bank.Code, returnType.Code, period)] = values;
        }
    }

    private Submission CreateAndFill(
        DemoBank bank, ReturnObligation obligation, TemplateVersion template,
        IReadOnlyDictionary<string, decimal> values, DateTimeOffset at, string context)
    {
        var maker = _users.Maker(bank.Code);
        var created = Submission.CreateDraft(obligation, template, maker, SubmissionSource.Web, at);
        Ensure(created, context);
        var submission = created.Value;
        Ensure(submission.SetValues(template, AsInput(values), maker, at), context);
        return submission;
    }

    private void ValidateAndSubmit(
        DemoBank bank, ReturnType returnType, TemplateVersion template, Submission submission, ReturnObligation obligation,
        IReadOnlyDictionary<string, decimal> values, ReportingPeriod period, int periodsAgo, DateTimeOffset submittedAt, string context)
    {
        var warnings = SeedFindingCalculator.Warnings(template, values, basis => PriorValues(bank, returnType, period, basis));
        Ensure(submission.RecordValidation(warnings), context);

        var checker = _users.Checker(bank.Code);
        foreach (var finding in submission.CurrentFindings.ToList())
        {
            var justification = DemoScenario.JustificationFor(bank.Code, returnType.Code, periodsAgo, finding.RuleCode)
                ?? throw new InvalidOperationException(
                    $"Seed data for {context} trips warning {finding.RuleCode} with no scripted justification.");
            Ensure(submission.JustifyWarning(finding.Id, justification, checker, submittedAt.AddHours(-2)), context);
        }

        Ensure(submission.Submit(checker, obligation, DemoScenario.SubmitComment, submittedAt), context);
    }

    private void RunReturnedForCorrection(
        DemoBank bank, ReturnType returnType, TemplateVersion template, Submission submission, ReturnObligation obligation,
        IReadOnlyDictionary<string, decimal> correctedValues, ReportingPeriod period, int periodsAgo, SeedClock clock)
    {
        var context = $"{bank.Code} {returnType.Code} {period.Label} (returned)";
        var revision1 = submission.Values.ToDictionary(v => v.FieldCode, v => v.NumericValue!.Value, StringComparer.Ordinal);
        ValidateAndSubmit(bank, returnType, template, submission, obligation, revision1, period, periodsAgo, clock.Submitted, context);

        var reviewer = _users.Actor(DemoUsers.Reviewer);
        Ensure(submission.StartReview(reviewer, clock.Submitted.AddDays(1)), context);
        Ensure(submission.ReturnForCorrection(reviewer, DemoScenario.CrestmontReturnComment, clock.Submitted.AddDays(1).AddHours(3)), context);

        var maker = _users.Maker(bank.Code);
        var correctedAt = clock.Submitted.AddDays(2);
        Ensure(submission.SetValues(template, AsInput(correctedValues), maker, correctedAt), context);
        ValidateAndSubmit(bank, returnType, template, submission, obligation, correctedValues, period, periodsAgo, correctedAt.AddHours(5), context);
    }

    private IReadOnlyDictionary<string, decimal>? PriorValues(DemoBank bank, ReturnType returnType, ReportingPeriod period, VarianceBasis basis)
    {
        var prior = basis == VarianceBasis.PreviousPeriod ? period.Previous() : period.SamePeriodLastYear();
        return _approved.GetValueOrDefault((bank.Code, returnType.Code, prior));
    }

    private static SeedClock HistoryClock(ReturnObligation obligation, DemoScenario.Plan plan)
    {
        var created = At(obligation.Period.End.AddDays(2), 9);
        var daysAfterDue = plan.ReturnedOnce ? -6 : -3;
        if (plan.LateDays > 0)
        {
            daysAfterDue = plan.LateDays;
        }

        var submitted = At(obligation.DueDate.AddDays(daysAfterDue), 14);

        // A returned submission is corrected and resubmitted two days later, so its final review starts after that.
        var reviewStarted = submitted.AddDays(plan.ReturnedOnce ? 3 : 1).AddHours(-4);
        return new SeedClock(created, submitted, reviewStarted, reviewStarted.AddDays(1).AddHours(2));
    }

    // In-flight returns are spread between the end of the period and now, so nothing is dated in the future.
    private SeedClock InFlightClock(ReportingPeriod period)
    {
        var start = At(period.End.AddDays(1), 0);
        var span = now - start;
        return new SeedClock(start + (span * 0.2), start + (span * 0.4), start + (span * 0.6), start + (span * 0.8));
    }

    private static DateTimeOffset At(DateOnly date, int hour) => new(date.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero);

    private sealed record SeedClock(DateTimeOffset Created, DateTimeOffset Submitted, DateTimeOffset ReviewStarted, DateTimeOffset Decided);
}
