using RegReturns.Domain.Periods;
using RegReturns.Domain.Templates;

namespace RegReturns.Infrastructure.Persistence.Seeding;

/// <summary>Quarterly Capital Adequacy Return: capital tiers, risk-weighted assets and capital ratios.</summary>
internal static class QcarTemplate
{
    public const string Code = "QCAR";
    public const string Cet1 = "CET1";
    public const string At1 = "AT1";
    public const string Tier1 = "TIER1";
    public const string Tier2 = "TIER2";
    public const string TotalCapital = "TOTAL_CAPITAL";
    public const string RwaCredit = "RWA_CREDIT";
    public const string RwaMarket = "RWA_MARKET";
    public const string RwaOperational = "RWA_OPERATIONAL";
    public const string TotalRwa = "TOTAL_RWA";
    public const string Cet1Ratio = "CET1_RATIO";
    public const string Tier1Ratio = "TIER1_RATIO";
    public const string Car = "CAR";

    public static ReturnType CreateReturnType() => ReturnType.Create(
        Code,
        "Quarterly Capital Adequacy Return",
        "Common Equity Tier 1, Additional Tier 1 and Tier 2 capital, risk-weighted assets by risk type, and capital ratios.",
        ReturnFrequency.Quarterly,
        dueDaysAfterPeriodEnd: 30);

    public static TemplateVersion CreateTemplate(ReturnType returnType, DateOnly effectiveFrom)
    {
        const string capital = "Capital";
        const string rwa = "Risk-weighted assets";
        const string ratios = "Capital ratios";

        return new TemplateBuilder(TemplateVersion.CreateDraft(returnType.Id, 1, effectiveFrom))
            .Amount(Cet1, "Common Equity Tier 1 capital", capital)
            .Amount(At1, "Additional Tier 1 capital", capital)
            .Amount(Tier1, "Tier 1 capital", capital)
            .Amount(Tier2, "Tier 2 capital", capital)
            .Amount(TotalCapital, "Total capital", capital)
            .Amount(RwaCredit, "Credit risk RWA", rwa)
            .Amount(RwaMarket, "Market risk RWA", rwa)
            .Amount(RwaOperational, "Operational risk RWA", rwa)
            .Amount(TotalRwa, "Total RWA", rwa)
            .Percentage(Cet1Ratio, "CET1 ratio", ratios)
            .Percentage(Tier1Ratio, "Tier 1 ratio", ratios)
            .Percentage(Car, "Total capital adequacy ratio", ratios)
            .StandardFieldRules(Code)
            .Rule(ValidationRule.CrossField("QCAR_T1_SUM", Tier1, Severity.Error,
                $"[{Tier1}]", ComparisonOperator.Equal, $"[{Cet1}] + [{At1}]", 0.05m, "Tier 1 must equal CET1 + AT1."))
            .Rule(ValidationRule.CrossField("QCAR_TC_SUM", TotalCapital, Severity.Error,
                $"[{TotalCapital}]", ComparisonOperator.Equal, $"[{Tier1}] + [{Tier2}]", 0.05m,
                "Total capital must equal Tier 1 + Tier 2."))
            .Rule(ValidationRule.CrossField("QCAR_RWA_SUM", TotalRwa, Severity.Error,
                $"[{TotalRwa}]", ComparisonOperator.Equal, $"[{RwaCredit}] + [{RwaMarket}] + [{RwaOperational}]", 0.05m,
                "Total RWA must equal credit + market + operational RWA."))
            .Rule(ValidationRule.CrossField("QCAR_CET1R_CALC", Cet1Ratio, Severity.Error,
                $"[{Cet1Ratio}]", ComparisonOperator.Equal, $"[{Cet1}] / [{TotalRwa}] * 100", 0.01m,
                "CET1 ratio must equal CET1 / Total RWA x 100."))
            .Rule(ValidationRule.CrossField("QCAR_T1R_CALC", Tier1Ratio, Severity.Error,
                $"[{Tier1Ratio}]", ComparisonOperator.Equal, $"[{Tier1}] / [{TotalRwa}] * 100", 0.01m,
                "Tier 1 ratio must equal Tier 1 / Total RWA x 100."))
            .Rule(ValidationRule.CrossField("QCAR_CAR_CALC", Car, Severity.Error,
                $"[{Car}]", ComparisonOperator.Equal, $"[{TotalCapital}] / [{TotalRwa}] * 100", 0.01m,
                "Capital adequacy ratio must equal Total capital / Total RWA x 100."))
            .Rule(ValidationRule.Range("QCAR_CET1R_MIN", Cet1Ratio, Severity.Warning, 7m, null,
                "CET1 ratio is below the 7% minimum."))
            .Rule(ValidationRule.Range("QCAR_CAR_MIN", Car, Severity.Warning, 12.5m, null,
                "Capital adequacy ratio is below the 12.5% minimum including buffers."))
            .Rule(ValidationRule.Variance("QCAR_CAR_VAR", Car, Severity.Warning, 15m, VarianceBasis.PreviousPeriod,
                "Capital adequacy ratio moved by more than 15% against the previous quarter."))
            .Rule(ValidationRule.Variance("QCAR_RWA_VAR", TotalRwa, Severity.Warning, 20m, VarianceBasis.SamePeriodLastYear,
                "Total RWA moved by more than 20% against the same quarter last year."))
            .Publish();
    }
}
