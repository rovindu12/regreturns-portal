namespace RegReturns.Domain.Institutions;

/// <summary>The banking licence an institution holds.</summary>
public enum LicenceCategory
{
    /// <summary>Licensed commercial bank.</summary>
    Commercial = 1,

    /// <summary>Licensed savings bank.</summary>
    Savings = 2,

    /// <summary>Licensed development bank.</summary>
    Development = 3,
}
