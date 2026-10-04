using RegReturns.Domain.Periods;
using RegReturns.Domain.Templates;

namespace RegReturns.Infrastructure.Persistence.Seeding;

/// <summary>Monthly Liquidity Return: high-quality liquid assets, 30-day cash flows and the liquidity coverage ratio.</summary>
internal static class MlrTemplate
{
    public const string Code = "MLR";
    public const string L1Hqla = "L1_HQLA";
    public const string L2aHqla = "L2A_HQLA";
    public const string L2bHqla = "L2B_HQLA";
    public const string TotalHqla = "TOTAL_HQLA";
    public const string Outflows = "CASH_OUTFLOWS_30D";
    public const string Inflows = "CASH_INFLOWS_30D";
    public const string NetOutflows = "NET_CASH_OUTFLOWS";
    public const string Lcr = "LCR";
    public const string TotalDeposits = "TOTAL_DEPOSITS";
    public const string LiquidAssets = "LIQUID_ASSETS";
    public const string LiquidAssetsRatio = "LIQUID_ASSETS_RATIO";

    public const string RuleL2bCap = "MLR_L2B_CAP";
    public const string RuleLcrMinimum = "MLR_LCR_MIN";
    public const string RuleLcrVariance = "MLR_LCR_VAR";
    public const string RuleHqlaVariance = "MLR_HQLA_VAR";

    public static ReturnType CreateReturnType() => ReturnType.Create(
        Code,
        "Monthly Liquidity Return",
        "High-quality liquid assets, 30-day stressed cash flows, the liquidity coverage ratio and the statutory liquid assets ratio.",
        ReturnFrequency.Monthly,
        dueDaysAfterPeriodEnd: 15);

    public static TemplateVersion CreateTemplate(ReturnType returnType, DateOnly effectiveFrom)
    {
        const string hqla = "High-quality liquid assets";
        const string flows = "30-day stressed cash flows";
        const string ratios = "Ratios";
        const string deposits = "Deposits and liquid assets";

        return new TemplateBuilder(TemplateVersion.CreateDraft(returnType.Id, 1, effectiveFrom))
            .Amount(L1Hqla, "Level 1 HQLA", hqla)
            .Amount(L2aHqla, "Level 2A HQLA", hqla)
            .Amount(L2bHqla, "Level 2B HQLA", hqla)
            .Amount(TotalHqla, "Total HQLA", hqla)
            .Amount(Outflows, "Total cash outflows", flows)
            .Amount(Inflows, "Total cash inflows", flows)
            .Amount(NetOutflows, "Net cash outflows", flows)
            .Percentage(Lcr, "Liquidity coverage ratio", ratios)
            .Amount(TotalDeposits, "Total deposits", deposits)
            .Amount(LiquidAssets, "Statutory liquid assets", deposits)
            .Percentage(LiquidAssetsRatio, "Liquid assets ratio", ratios)
            .StandardFieldRules(Code)
            .Rule(ValidationRule.CrossField("MLR_HQLA_SUM", TotalHqla, Severity.Error,
                $"[{TotalHqla}]", ComparisonOperator.Equal, $"[{L1Hqla}] + [{L2aHqla}] + [{L2bHqla}]", 0.05m,
                "Total HQLA must equal Level 1 + Level 2A + Level 2B."))
            .Rule(ValidationRule.CrossField("MLR_NCO_CALC", NetOutflows, Severity.Error,
                $"[{NetOutflows}]", ComparisonOperator.Equal, $"[{Outflows}] - Min([{Inflows}], 0.75 * [{Outflows}])", 0.05m,
                "Net cash outflows must equal outflows minus inflows, with inflows capped at 75% of outflows."))
            .Rule(ValidationRule.CrossField("MLR_LCR_CALC", Lcr, Severity.Error,
                $"[{Lcr}]", ComparisonOperator.Equal, $"[{TotalHqla}] / [{NetOutflows}] * 100", 0.01m,
                "LCR must equal Total HQLA / Net cash outflows x 100."))
            .Rule(ValidationRule.CrossField("MLR_LAR_CALC", LiquidAssetsRatio, Severity.Error,
                $"[{LiquidAssetsRatio}]", ComparisonOperator.Equal, $"[{LiquidAssets}] / [{TotalDeposits}] * 100", 0.01m,
                "Liquid assets ratio must equal Liquid assets / Total deposits x 100."))
            .Rule(ValidationRule.CrossField(RuleL2bCap, L2bHqla, Severity.Warning,
                $"[{L2bHqla}]", ComparisonOperator.LessThanOrEqual, $"0.15 * [{TotalHqla}]", 0m,
                "Level 2B assets exceed the 15% cap of Total HQLA."))
            .Rule(ValidationRule.Range(RuleLcrMinimum, Lcr, Severity.Warning, 100m, null,
                "LCR is below the 100% regulatory minimum."))
            .Rule(ValidationRule.Range("MLR_LAR_MIN", LiquidAssetsRatio, Severity.Warning, 20m, null,
                "Liquid assets ratio is below the 20% statutory minimum."))
            .Rule(ValidationRule.Variance(RuleLcrVariance, Lcr, Severity.Warning, 25m, VarianceBasis.PreviousPeriod,
                "LCR moved by more than 25% against the previous month."))
            .Rule(ValidationRule.Variance(RuleHqlaVariance, TotalHqla, Severity.Warning, 30m, VarianceBasis.PreviousPeriod,
                "Total HQLA moved by more than 30% against the previous month."))
            .Rule(ValidationRule.Variance("MLR_DEP_VAR", TotalDeposits, Severity.Warning, 15m, VarianceBasis.PreviousPeriod,
                "Total deposits moved by more than 15% against the previous month."))
            .Publish();
    }
}
