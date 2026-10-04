using RegReturns.Domain.Institutions;

namespace RegReturns.Infrastructure.Persistence.Seeding;

/// <summary>A fictional bank in the demo, with the size and growth used to generate its figures.</summary>
/// <param name="Code">Institution code (the WSO2 <c>institution_id</c> claim value).</param>
/// <param name="Name">Registered name.</param>
/// <param name="Domain">E-mail domain for its demo users (reserved <c>.example</c> TLD).</param>
/// <param name="Category">Licence category.</param>
/// <param name="Scale">Balance-sheet size relative to the largest bank.</param>
/// <param name="MonthlyGrowth">Average month-on-month deposit growth.</param>
internal sealed record DemoBank(
    string Code, string Name, string Domain, LicenceCategory Category, decimal Scale, decimal MonthlyGrowth)
{
    public const string Harbourline = "HLB";
    public const string Crestmont = "CCB";
    public const string LotusUnion = "LUB";
    public const string Northgate = "NSB";
    public const string Meridian = "MDB";

    /// <summary>The five demo banks. All names are fictional.</summary>
    public static IReadOnlyList<DemoBank> All { get; } =
    [
        new(Harbourline, "Harbourline Bank PLC", "harbourline.example", LicenceCategory.Commercial, 1.00m, 0.0060m),
        new(Crestmont, "Crestmont Commercial Bank", "crestmont.example", LicenceCategory.Commercial, 0.80m, 0.0050m),
        new(LotusUnion, "Lotus Union Bank", "lotusunion.example", LicenceCategory.Commercial, 0.60m, 0.0070m),
        new(Northgate, "Northgate Savings Bank", "northgate.example", LicenceCategory.Savings, 0.35m, 0.0040m),
        new(Meridian, "Meridian Development Bank", "meridian.example", LicenceCategory.Development, 0.25m, 0.0030m),
    ];

    /// <summary>Gets the index of the bank in <see cref="All"/>, used to seed its random figures.</summary>
    public int Index => All.ToList().FindIndex(b => b.Code == Code);
}
