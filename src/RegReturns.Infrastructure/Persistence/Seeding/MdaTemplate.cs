using RegReturns.Domain.Periods;
using RegReturns.Domain.Templates;

namespace RegReturns.Infrastructure.Persistence.Seeding;

/// <summary>Monthly Deposits and Advances Return: deposits by type, loans by sector and non-performing loans.</summary>
internal static class MdaTemplate
{
    public const string Code = "MDA";
    public const string DepDemand = "DEP_DEMAND";
    public const string DepSavings = "DEP_SAVINGS";
    public const string DepTime = "DEP_TIME";
    public const string TotalDeposits = "TOTAL_DEPOSITS";
    public const string LoansAgriculture = "LOANS_AGRICULTURE";
    public const string LoansManufacturing = "LOANS_MANUFACTURING";
    public const string LoansTrade = "LOANS_TRADE";
    public const string LoansPersonal = "LOANS_PERSONAL";
    public const string LoansOther = "LOANS_OTHER";
    public const string TotalLoans = "TOTAL_LOANS";
    public const string NplAmount = "NPL_AMOUNT";
    public const string NplRatio = "NPL_RATIO";

    public const string RuleNplMaximum = "MDA_NPL_MAX";
    public const string RuleNplVariance = "MDA_NPL_VAR";

    public static ReturnType CreateReturnType() => ReturnType.Create(
        Code,
        "Monthly Deposits and Advances Return",
        "Deposits by product, loans and advances by economic sector, and non-performing loans.",
        ReturnFrequency.Monthly,
        dueDaysAfterPeriodEnd: 21);

    public static TemplateVersion CreateTemplate(ReturnType returnType, DateOnly effectiveFrom)
    {
        const string deposits = "Deposits";
        const string loans = "Loans and advances by sector";
        const string quality = "Asset quality";

        return new TemplateBuilder(TemplateVersion.CreateDraft(returnType.Id, 1, effectiveFrom))
            .Amount(DepDemand, "Demand deposits", deposits)
            .Amount(DepSavings, "Savings deposits", deposits)
            .Amount(DepTime, "Time deposits", deposits)
            .Amount(TotalDeposits, "Total deposits", deposits)
            .Amount(LoansAgriculture, "Agriculture", loans)
            .Amount(LoansManufacturing, "Manufacturing", loans)
            .Amount(LoansTrade, "Trade and services", loans)
            .Amount(LoansPersonal, "Personal and housing", loans)
            .Amount(LoansOther, "Other sectors", loans)
            .Amount(TotalLoans, "Total loans and advances", loans)
            .Amount(NplAmount, "Non-performing loans", quality)
            .Percentage(NplRatio, "Non-performing loan ratio", quality)
            .StandardFieldRules(Code)
            .Rule(ValidationRule.CrossField("MDA_DEP_SUM", TotalDeposits, Severity.Error,
                $"[{TotalDeposits}]", ComparisonOperator.Equal, $"[{DepDemand}] + [{DepSavings}] + [{DepTime}]", 0.05m,
                "Total deposits must equal demand + savings + time deposits."))
            .Rule(ValidationRule.CrossField("MDA_LOAN_SUM", TotalLoans, Severity.Error,
                $"[{TotalLoans}]", ComparisonOperator.Equal,
                $"[{LoansAgriculture}] + [{LoansManufacturing}] + [{LoansTrade}] + [{LoansPersonal}] + [{LoansOther}]", 0.05m,
                "Total loans must equal the sum of all sectors."))
            .Rule(ValidationRule.CrossField("MDA_NPL_LE_LOANS", NplAmount, Severity.Error,
                $"[{NplAmount}]", ComparisonOperator.LessThanOrEqual, $"[{TotalLoans}]", 0m,
                "Non-performing loans cannot exceed total loans."))
            .Rule(ValidationRule.CrossField("MDA_NPLR_CALC", NplRatio, Severity.Error,
                $"[{NplRatio}]", ComparisonOperator.Equal, $"[{NplAmount}] / [{TotalLoans}] * 100", 0.01m,
                "NPL ratio must equal Non-performing loans / Total loans x 100."))
            .Rule(ValidationRule.Range(RuleNplMaximum, NplRatio, Severity.Warning, null, 8m,
                "NPL ratio is above the 8% supervisory trigger."))
            .Rule(ValidationRule.Variance(RuleNplVariance, NplRatio, Severity.Warning, 40m, VarianceBasis.PreviousPeriod,
                "NPL ratio moved by more than 40% against the previous month."))
            .Rule(ValidationRule.Variance("MDA_LOAN_VAR", TotalLoans, Severity.Warning, 15m, VarianceBasis.PreviousPeriod,
                "Total loans moved by more than 15% against the previous month."))
            .Publish();
    }
}
