using RegReturns.Domain.Periods;

namespace RegReturns.Infrastructure.Persistence.Seeding;

/// <summary>
/// Generates realistic, internally consistent return figures (VLD millions).
/// Figures depend only on the bank and the period, so every run produces the same numbers.
/// Totals and ratios are computed from rounded components, so cross-field rules always pass.
/// </summary>
internal static class FigureGenerator
{
    private const decimal BaseDeposits = 450_000m;
    private const int BaseOrdinal = 2020 * 12;

    /// <summary>Shape adjustments used to plant liquidity anomalies.</summary>
    /// <param name="Level1Factor">Multiplier on Level 1 HQLA.</param>
    /// <param name="Level2AFactor">Multiplier on Level 2A HQLA.</param>
    /// <param name="Level2BShare">Level 2B HQLA as a share of deposits.</param>
    public sealed record MlrShape(decimal Level1Factor = 1m, decimal Level2AFactor = 1m, decimal Level2BShare = 0.011m)
    {
        public static MlrShape Normal { get; } = new();
    }

    public static Dictionary<string, decimal> Mlr(DemoBank bank, ReportingPeriod month, MlrShape shape)
    {
        var deposits = Deposits(bank, month).Total;
        var n = new Noise(bank, month);

        var l1 = Round(deposits * 0.22m * shape.Level1Factor * n.Next(0.03m));
        var l2a = Round(deposits * 0.05m * shape.Level2AFactor * n.Next(0.05m));
        var l2b = Round(deposits * shape.Level2BShare * n.Next(0.05m));
        var totalHqla = l1 + l2a + l2b;
        var outflows = Round(deposits * 0.18m * n.Next(0.03m));
        var inflows = Round(deposits * 0.05m * n.Next(0.05m));
        var netOutflows = outflows - Math.Min(inflows, 0.75m * outflows);
        var liquidAssets = Round(deposits * 0.30m * n.Next(0.03m));

        return new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [MlrTemplate.L1Hqla] = l1,
            [MlrTemplate.L2aHqla] = l2a,
            [MlrTemplate.L2bHqla] = l2b,
            [MlrTemplate.TotalHqla] = totalHqla,
            [MlrTemplate.Outflows] = outflows,
            [MlrTemplate.Inflows] = inflows,
            [MlrTemplate.NetOutflows] = netOutflows,
            [MlrTemplate.Lcr] = Ratio(totalHqla, netOutflows),
            [MlrTemplate.TotalDeposits] = deposits,
            [MlrTemplate.LiquidAssets] = liquidAssets,
            [MlrTemplate.LiquidAssetsRatio] = Ratio(liquidAssets, deposits),
        };
    }

    /// <summary>Monthly Deposits and Advances figures.</summary>
    /// <param name="bank">The bank.</param>
    /// <param name="month">The month.</param>
    /// <param name="nplRatioPercent">Target NPL ratio in percent; defaults to the bank's normal level.</param>
    public static Dictionary<string, decimal> Mda(DemoBank bank, ReportingPeriod month, decimal? nplRatioPercent = null)
    {
        var deposits = Deposits(bank, month);
        var n = new Noise(bank, month, salt: 2);

        var loanBase = deposits.Total * 0.78m * n.Next(0.02m);
        var agriculture = Round(loanBase * 0.10m * n.Next(0.04m));
        var manufacturing = Round(loanBase * 0.22m * n.Next(0.04m));
        var trade = Round(loanBase * 0.25m * n.Next(0.04m));
        var personal = Round(loanBase * 0.28m * n.Next(0.04m));
        var other = Round(loanBase * 0.15m * n.Next(0.04m));
        var totalLoans = agriculture + manufacturing + trade + personal + other;
        var npl = Round(totalLoans * (nplRatioPercent ?? (3.6m + (bank.Index * 0.4m))) / 100m * n.Next(0.03m));

        return new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [MdaTemplate.DepDemand] = deposits.Demand,
            [MdaTemplate.DepSavings] = deposits.Savings,
            [MdaTemplate.DepTime] = deposits.Time,
            [MdaTemplate.TotalDeposits] = deposits.Total,
            [MdaTemplate.LoansAgriculture] = agriculture,
            [MdaTemplate.LoansManufacturing] = manufacturing,
            [MdaTemplate.LoansTrade] = trade,
            [MdaTemplate.LoansPersonal] = personal,
            [MdaTemplate.LoansOther] = other,
            [MdaTemplate.TotalLoans] = totalLoans,
            [MdaTemplate.NplAmount] = npl,
            [MdaTemplate.NplRatio] = Ratio(npl, totalLoans),
        };
    }

    public static Dictionary<string, decimal> Qcar(DemoBank bank, ReportingPeriod quarter)
    {
        var quarterEndMonth = ReportingPeriod.Containing(ReturnFrequency.Monthly, quarter.End);
        var deposits = Deposits(bank, quarterEndMonth).Total;
        var n = new Noise(bank, quarter, salt: 3);

        var rwaCredit = Round(deposits * 0.55m * n.Next(0.02m));
        var rwaMarket = Round(deposits * 0.03m * n.Next(0.05m));
        var rwaOperational = Round(deposits * 0.07m * n.Next(0.02m));
        var totalRwa = rwaCredit + rwaMarket + rwaOperational;
        var cet1 = Round(totalRwa * 0.125m * n.Next(0.02m));
        var at1 = Round(totalRwa * 0.015m * n.Next(0.05m));
        var tier1 = cet1 + at1;
        var tier2 = Round(totalRwa * 0.03m * n.Next(0.05m));
        var totalCapital = tier1 + tier2;

        return new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [QcarTemplate.Cet1] = cet1,
            [QcarTemplate.At1] = at1,
            [QcarTemplate.Tier1] = tier1,
            [QcarTemplate.Tier2] = tier2,
            [QcarTemplate.TotalCapital] = totalCapital,
            [QcarTemplate.RwaCredit] = rwaCredit,
            [QcarTemplate.RwaMarket] = rwaMarket,
            [QcarTemplate.RwaOperational] = rwaOperational,
            [QcarTemplate.TotalRwa] = totalRwa,
            [QcarTemplate.Cet1Ratio] = Ratio(cet1, totalRwa),
            [QcarTemplate.Tier1Ratio] = Ratio(tier1, totalRwa),
            [QcarTemplate.Car] = Ratio(totalCapital, totalRwa),
        };
    }

    private static (decimal Demand, decimal Savings, decimal Time, decimal Total) Deposits(DemoBank bank, ReportingPeriod month)
    {
        var monthsSinceBase = (month.Year * 12) + month.Number - BaseOrdinal;
        var trend = BaseDeposits * bank.Scale * Compound(1m + bank.MonthlyGrowth, monthsSinceBase);
        var n = new Noise(bank, month, salt: 1);
        var demand = Round(trend * 0.25m * n.Next(0.02m));
        var savings = Round(trend * 0.40m * n.Next(0.02m));
        var time = Round(trend * 0.35m * n.Next(0.02m));
        return (demand, savings, time, demand + savings + time);
    }

    private static decimal Compound(decimal factor, int periods)
    {
        var result = 1m;
        for (var i = 0; i < periods; i++)
        {
            result *= factor;
        }

        return result;
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.ToEven);

    private static decimal Ratio(decimal numerator, decimal denominator) =>
        Math.Round(numerator / denominator * 100m, 2, MidpointRounding.ToEven);

    /// <summary>Deterministic multiplicative noise, seeded from the bank and period only.</summary>
    private sealed class Noise(DemoBank bank, ReportingPeriod period, int salt = 0)
    {
        // Seeded per bank and period so the figures never depend on generation order.
        // Not used for anything security-related.
#pragma warning disable CA5394, S2245
        private readonly Random _random = new((bank.Index * 1_000_003) + (((period.Year * 12) + period.Number) * 7_919)
            + ((int)period.Frequency * 101) + salt);

        public decimal Next(decimal spread) => 1m + (((decimal)_random.NextDouble() * 2m) - 1m) * spread;
#pragma warning restore CA5394, S2245
    }
}
