using RegReturns.Application.Migration;
using RegReturns.Domain.Migration;

namespace RegReturns.UnitTests.TestSupport;

/// <summary>Legacy migration results for the report and console tests: two files, two banks and one quarter.</summary>
internal static class MigrationResults
{
    public static readonly Guid RunId = Guid.Parse("0199b2c3-d4e5-7f60-8a7b-9c0d1e2f3a4b");

    public static readonly string MlrSha = new('a', 64);

    public static readonly string QcarSha = new('b', 64);

    /// <summary>MLR: 6 rows read, 1 blank, 1 superseded, 1 rejected, 2 migrated and 1 already migrated.</summary>
    public static readonly MigrationTotals MlrTotals = new(6, 1, 1, 1, 2, 1);

    /// <summary>QCAR: 3 rows read, all migrated.</summary>
    public static readonly MigrationTotals QcarTotals = new(3, 0, 0, 0, 3, 0);

    public static readonly MigrationTotals AllTotals = new(9, 1, 1, 1, 5, 1);

    public static IReadOnlyList<LegacyFileSummary> Files { get; } =
    [
        new("mlr.csv", "MLR", MlrSha, MlrTotals),
        new("qcar.csv", "QCAR", QcarSha, QcarTotals),
    ];

    public static IReadOnlyList<LegacyRowError> Errors { get; } =
    [
        new("mlr.csv", 3, RowErrorKind.Superseded, MigrationErrors.Superseded, "Replaced by line 6, a later row for HLB MLR 2024-01; the last row wins."),
        new("mlr.csv", 5, RowErrorKind.Rejected, MigrationErrors.BadNumber, "The value of L1_HQLA is not a number.", "Lvl 1 Assets", "=SUM(A1:A9)"),
    ];

    /// <summary>Three returns that reconcile exactly.</summary>
    public static Reconciliation Reconciled { get; } = Reconciliation.Compare(Source, Source);

    /// <summary>The same returns with one value off by 0.25 and one return missing from the target.</summary>
    public static Reconciliation Mismatched { get; } = Reconciliation.Compare(
        Source,
        [
            new(new("MLR", "HLB", "2024-01"), Values(("TOTAL_HQLA", 1234.25m), ("LCR", 120m))),
            new(new("QCAR", "HLB", "2024-Q1"), Values(("CAR", 15.5m))),
        ]);

    private static IEnumerable<ReconciledReturn> Source =>
    [
        new(new("MLR", "HLB", "2024-01"), Values(("TOTAL_HQLA", 1234.5m), ("LCR", 120m))),
        new(new("MLR", "CCB", "2024-01"), Values(("TOTAL_HQLA", 1000m), ("LCR", 110m))),
        new(new("QCAR", "HLB", "2024-Q1"), Values(("CAR", 15.5m))),
    ];

    public static LegacyMigrationResult Create(
        MigrationOutcome outcome = MigrationOutcome.Reconciled,
        bool isDryRun = false,
        bool committed = true,
        MigrationTotals? totals = null,
        IReadOnlyList<LegacyRowError>? errors = null,
        Reconciliation? reconciliation = null) =>
        new(RunId, isDryRun, committed, outcome, totals ?? AllTotals, Files, errors ?? Errors, reconciliation ?? Reconciled);

    private static Dictionary<string, decimal?> Values(params (string Field, decimal? Value)[] values) =>
        values.ToDictionary(v => v.Field, v => v.Value, StringComparer.Ordinal);
}
