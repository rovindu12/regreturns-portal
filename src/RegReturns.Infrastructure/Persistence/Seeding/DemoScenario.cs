namespace RegReturns.Infrastructure.Persistence.Seeding;

/// <summary>
/// The scripted story behind the demo data: which returns are late, missing, sent back or in flight,
/// and the justifications banks gave for the warnings their figures trip.
/// "Periods ago" counts back from the latest completed period (0 = the return currently being filed).
/// </summary>
internal static class DemoScenario
{
    public const int LotusLiquidityShockMonthsAgo = 3;
    public const int CrestmontReturnedMonthsAgo = 6;
    public const int HarbourlineNplJumpMonthsAgo = 2;
    public const int MeridianMissingMonthsAgo = 5;
    public const int NorthgateFirstLateQuartersAgo = 3;
    public const int NorthgateSecondLateQuartersAgo = 1;

    public const string SubmitComment = "Figures checked against the general ledger and approved for submission.";
    public const string ApprovalComment = "Reviewed against prior periods and supervisory thresholds. Approved.";
    public const string CrestmontReturnComment =
        "Listed corporate equities are reported as Level 2B HQLA above the 15% cap. Reclassify them and resubmit.";

    private static readonly Dictionary<(string Bank, string Return, int PeriodsAgo, string Rule), string> Justifications = new()
    {
        [(DemoBank.LotusUnion, MlrTemplate.Code, LotusLiquidityShockMonthsAgo, MlrTemplate.RuleLcrMinimum)] =
            "A single corporate depositor withdrew VLD 1.2bn on the last business day of the month. Contingency funding plan activated; LCR restored above 100% within ten days.",
        [(DemoBank.LotusUnion, MlrTemplate.Code, LotusLiquidityShockMonthsAgo, MlrTemplate.RuleLcrVariance)] =
            "LCR fell because of the one-off corporate deposit withdrawal described in the LCR minimum justification.",
        [(DemoBank.LotusUnion, MlrTemplate.Code, LotusLiquidityShockMonthsAgo, MlrTemplate.RuleHqlaVariance)] =
            "Level 1 and Level 2A securities were sold and repo'd to meet the corporate withdrawal; HQLA is being rebuilt.",
        [(DemoBank.LotusUnion, MlrTemplate.Code, LotusLiquidityShockMonthsAgo - 1, MlrTemplate.RuleLcrVariance)] =
            "LCR recovered to its normal range after HQLA was rebuilt following last month's corporate withdrawal.",
        [(DemoBank.LotusUnion, MlrTemplate.Code, LotusLiquidityShockMonthsAgo - 1, MlrTemplate.RuleHqlaVariance)] =
            "Government securities repurchased after last month's corporate withdrawal; HQLA back to its usual level.",
        [(DemoBank.Crestmont, MlrTemplate.Code, CrestmontReturnedMonthsAgo, MlrTemplate.RuleL2bCap)] =
            "Includes listed corporate equities bought this month, which we consider eligible Level 2B assets.",
        [(DemoBank.Harbourline, MdaTemplate.Code, HarbourlineNplJumpMonthsAgo, MdaTemplate.RuleNplMaximum)] =
            "Two large manufacturing exposures were reclassified as non-performing after missed instalments. Provisions raised in full.",
        [(DemoBank.Harbourline, MdaTemplate.Code, HarbourlineNplJumpMonthsAgo, MdaTemplate.RuleNplVariance)] =
            "Increase driven by the reclassification of two large manufacturing exposures; see the NPL maximum justification.",
    };

    /// <summary>What should happen to a return in the demo story.</summary>
    public enum Outcome
    {
        /// <summary>Nothing filed (the obligation stays open).</summary>
        NotFiled,

        /// <summary>The maker has a draft.</summary>
        Draft,

        /// <summary>Submitted, waiting for a reviewer.</summary>
        Submitted,

        /// <summary>Picked up by the reviewer.</summary>
        UnderReview,

        /// <summary>Approved.</summary>
        Approved,
    }

    /// <summary>The planned outcome for one return.</summary>
    /// <param name="Status">Final workflow outcome.</param>
    /// <param name="InFlight">Whether this is the current period being filed.</param>
    /// <param name="LateDays">Days after the due date the return was submitted (0 = on time).</param>
    /// <param name="ReturnedOnce">Whether the reviewer sent revision 1 back for correction.</param>
    public sealed record Plan(Outcome Status, bool InFlight = false, int LateDays = 0, bool ReturnedOnce = false);

    public static Plan PlanFor(string bank, string returnCode, int periodsAgo) => periodsAgo == 0
        ? new Plan(InFlightOutcome(bank, returnCode), InFlight: true)
        : (bank, returnCode, periodsAgo) switch
        {
            (DemoBank.Meridian, MdaTemplate.Code, MeridianMissingMonthsAgo) => new Plan(Outcome.NotFiled),
            (DemoBank.Crestmont, MlrTemplate.Code, CrestmontReturnedMonthsAgo) => new Plan(Outcome.Approved, ReturnedOnce: true),
            (DemoBank.Northgate, QcarTemplate.Code, NorthgateFirstLateQuartersAgo) => new Plan(Outcome.Approved, LateDays: 11),
            (DemoBank.Northgate, QcarTemplate.Code, NorthgateSecondLateQuartersAgo) => new Plan(Outcome.Approved, LateDays: 6),
            _ => new Plan(Outcome.Approved),
        };

    public static string? JustificationFor(string bank, string returnCode, int periodsAgo, string ruleCode) =>
        Justifications.GetValueOrDefault((bank, returnCode, periodsAgo, ruleCode));

    // The current period: a mix of states so every role has something in its queue.
    private static Outcome InFlightOutcome(string bank, string returnCode) => (bank, returnCode) switch
    {
        (DemoBank.Harbourline, MdaTemplate.Code) => Outcome.Draft,
        (DemoBank.Crestmont, MlrTemplate.Code) => Outcome.UnderReview,
        (DemoBank.Crestmont, MdaTemplate.Code) => Outcome.Submitted,
        (DemoBank.Crestmont, QcarTemplate.Code) => Outcome.Draft,
        (DemoBank.LotusUnion, MlrTemplate.Code) => Outcome.Submitted,
        (DemoBank.LotusUnion, QcarTemplate.Code) => Outcome.Submitted,
        (DemoBank.Northgate, MlrTemplate.Code) => Outcome.Approved,
        (DemoBank.Northgate, MdaTemplate.Code) => Outcome.Submitted,
        (DemoBank.Meridian, MlrTemplate.Code) => Outcome.Draft,
        _ => Outcome.NotFiled,
    };
}
