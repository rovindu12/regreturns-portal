namespace RegReturns.Application.Migration;

/// <summary>
/// Compares the values a migration took from the legacy files with the values the database holds afterwards, field by
/// field, and totals them by bank and by period (ADR 0029). Any difference, missing return or unexpected value is a
/// mismatch.
/// </summary>
public sealed class Reconciliation
{
    private Reconciliation(IReadOnlyList<ReconciliationLine> lines) => Lines = lines;

    /// <summary>Gets one line per field of every return, ordered by return type, bank and period.</summary>
    public IReadOnlyList<ReconciliationLine> Lines { get; }

    /// <summary>Gets the number of lines that do not match.</summary>
    public int Mismatches => Lines.Count(l => l.Status != ReconciliationStatus.Match);

    /// <summary>Gets the number of returns on the source side.</summary>
    public int SourceReturns => Lines.Where(l => l.Source is not null).Select(l => l.Key).Distinct().Count();

    /// <summary>Gets the number of returns on the target side.</summary>
    public int TargetReturns => Lines.Where(l => l.Target is not null).Select(l => l.Key).Distinct().Count();

    /// <summary>Compares source returns with target returns.</summary>
    /// <param name="source">The cleansed values taken from the legacy files.</param>
    /// <param name="target">The values read back from the database.</param>
    /// <returns>The reconciliation.</returns>
    public static Reconciliation Compare(IEnumerable<ReconciledReturn> source, IEnumerable<ReconciledReturn> target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        var sources = source.ToDictionary(r => r.Key);
        var targets = target.ToDictionary(r => r.Key);

        var lines = new List<ReconciliationLine>();
        var keys = sources.Keys.Union(targets.Keys)
            .OrderBy(k => k.ReturnTypeCode, StringComparer.Ordinal)
            .ThenBy(k => k.InstitutionCode, StringComparer.Ordinal)
            .ThenBy(k => k.Period, StringComparer.Ordinal);
        foreach (var key in keys)
        {
            var from = sources.GetValueOrDefault(key)?.Values ?? Empty;
            var to = targets.GetValueOrDefault(key)?.Values ?? Empty;
            foreach (var field in from.Keys.Concat(to.Keys.Where(k => !from.ContainsKey(k))))
            {
                lines.Add(new ReconciliationLine(key, field, from.GetValueOrDefault(field), to.GetValueOrDefault(field)));
            }
        }

        return new Reconciliation(lines);
    }

    /// <summary>Counts the returns on each side, and the lines that do not match, by return type and bank.</summary>
    /// <returns>The counts, in line order.</returns>
    public IReadOnlyList<ReconciliationCount> ReturnsByInstitution() =>
        [.. Lines
            .GroupBy(l => (l.Key.ReturnTypeCode, l.Key.InstitutionCode))
            .Select(g => new ReconciliationCount(
                g.Key.ReturnTypeCode,
                g.Key.InstitutionCode,
                g.Where(l => l.Source is not null).Select(l => l.Key).Distinct().Count(),
                g.Where(l => l.Target is not null).Select(l => l.Key).Distinct().Count(),
                g.Count(l => l.Status != ReconciliationStatus.Match)))];

    /// <summary>Totals every field by return type and bank.</summary>
    /// <returns>The totals, in line order.</returns>
    public IReadOnlyList<ReconciliationTotal> ByInstitution() => Totals(l => l.Key.InstitutionCode);

    /// <summary>Totals every field by return type and period.</summary>
    /// <returns>The totals, in period order.</returns>
    public IReadOnlyList<ReconciliationTotal> ByPeriod() =>
        [.. Totals(l => l.Key.Period).OrderBy(t => t.ReturnTypeCode, StringComparer.Ordinal).ThenBy(t => t.Group, StringComparer.Ordinal)];

    /// <summary>Totals every field by return type, across banks and periods.</summary>
    /// <returns>The totals, in line order.</returns>
    public IReadOnlyList<ReconciliationTotal> ByField() => Totals(_ => ReconciliationTotal.AllGroups);

    private static IReadOnlyDictionary<string, decimal?> Empty { get; } = new Dictionary<string, decimal?>();

    private List<ReconciliationTotal> Totals(Func<ReconciliationLine, string> group) =>
        [.. Lines
            .GroupBy(l => (l.Key.ReturnTypeCode, Group: group(l), l.FieldCode))
            .Select(g => new ReconciliationTotal(
                g.Key.ReturnTypeCode,
                g.Key.Group,
                g.Key.FieldCode,
                g.Count(l => l.Source is not null),
                g.Count(l => l.Target is not null),
                g.Sum(l => l.Source ?? 0m),
                g.Sum(l => l.Target ?? 0m)))];
}

/// <summary>Identifies a return: its type, bank and period label.</summary>
/// <param name="ReturnTypeCode">The return type code.</param>
/// <param name="InstitutionCode">The bank code.</param>
/// <param name="Period">The period label, such as <c>2024-01</c> or <c>2024-Q1</c>, which sorts in time order.</param>
public sealed record ReconciliationKey(string ReturnTypeCode, string InstitutionCode, string Period);

/// <summary>The values of one return on one side of a reconciliation.</summary>
/// <param name="Key">The return.</param>
/// <param name="Values">Values keyed by field code, in template order.</param>
public sealed record ReconciledReturn(ReconciliationKey Key, IReadOnlyDictionary<string, decimal?> Values);

/// <summary>How a reconciliation line compares.</summary>
public enum ReconciliationStatus
{
    /// <summary>Source and target hold the same value.</summary>
    Match = 1,

    /// <summary>Source and target hold different values.</summary>
    Mismatch = 2,

    /// <summary>The source has a value the target lacks.</summary>
    MissingInTarget = 3,

    /// <summary>The target has a value the source lacks.</summary>
    UnexpectedInTarget = 4,
}

/// <summary>One field of one return, source against target.</summary>
/// <param name="Key">The return.</param>
/// <param name="FieldCode">The field code.</param>
/// <param name="Source">The cleansed legacy value.</param>
/// <param name="Target">The value in the database.</param>
public sealed record ReconciliationLine(ReconciliationKey Key, string FieldCode, decimal? Source, decimal? Target)
{
    /// <summary>Gets target minus source, when both are present.</summary>
    public decimal? Difference => Source is { } s && Target is { } t ? t - s : null;

    /// <summary>Gets how the two sides compare.</summary>
    public ReconciliationStatus Status => (Source, Target) switch
    {
        ({ } s, { } t) => s == t ? ReconciliationStatus.Match : ReconciliationStatus.Mismatch,
        (not null, null) => ReconciliationStatus.MissingInTarget,
        (null, not null) => ReconciliationStatus.UnexpectedInTarget,
        _ => ReconciliationStatus.Match,
    };
}

/// <summary>A field's totals over a group of returns, source against target.</summary>
/// <param name="ReturnTypeCode">The return type.</param>
/// <param name="Group">The bank code, the period label or <see cref="AllGroups"/>.</param>
/// <param name="FieldCode">The field code.</param>
/// <param name="SourceReturns">Returns with a source value.</param>
/// <param name="TargetReturns">Returns with a target value.</param>
/// <param name="SourceTotal">The sum of the source values.</param>
/// <param name="TargetTotal">The sum of the target values.</param>
public sealed record ReconciliationTotal(
    string ReturnTypeCode, string Group, string FieldCode, int SourceReturns, int TargetReturns, decimal SourceTotal, decimal TargetTotal)
{
    /// <summary>The group name of totals across every bank and period.</summary>
    public const string AllGroups = "ALL";

    /// <summary>Gets target minus source.</summary>
    public decimal Difference => TargetTotal - SourceTotal;

    /// <summary>Gets a value indicating whether the counts and totals agree.</summary>
    public bool IsMatch => SourceReturns == TargetReturns && Difference == 0m;
}

/// <summary>The returns on each side of a reconciliation for a group, and how many of its lines differ.</summary>
/// <param name="ReturnTypeCode">The return type.</param>
/// <param name="Group">The bank code.</param>
/// <param name="SourceReturns">Returns on the source side.</param>
/// <param name="TargetReturns">Returns on the target side.</param>
/// <param name="Mismatches">Lines that do not match.</param>
public sealed record ReconciliationCount(string ReturnTypeCode, string Group, int SourceReturns, int TargetReturns, int Mismatches)
{
    /// <summary>Gets a value indicating whether both sides hold the same returns with the same values.</summary>
    public bool IsMatch => SourceReturns == TargetReturns && Mismatches == 0;
}
