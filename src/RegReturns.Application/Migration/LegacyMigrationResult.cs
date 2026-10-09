using RegReturns.Domain.Migration;

namespace RegReturns.Application.Migration;

/// <summary>The outcome of a legacy migration run, for the console summary and the CSV reports.</summary>
/// <param name="RunId">The <see cref="MigrationRun"/> id.</param>
/// <param name="IsDryRun">Whether the run rolled its changes back by design.</param>
/// <param name="Committed">Whether the migrated returns were committed.</param>
/// <param name="Outcome">Reconciled, or a mismatch.</param>
/// <param name="Totals">How every source row was accounted for.</param>
/// <param name="Files">Per-file accounting.</param>
/// <param name="Errors">Rejected and superseded rows, in file and line order.</param>
/// <param name="Reconciliation">Source and target values of every return the run migrated or found migrated.</param>
public sealed record LegacyMigrationResult(
    Guid RunId,
    bool IsDryRun,
    bool Committed,
    MigrationOutcome Outcome,
    MigrationTotals Totals,
    IReadOnlyList<LegacyFileSummary> Files,
    IReadOnlyList<LegacyRowError> Errors,
    Reconciliation Reconciliation);

/// <summary>One legacy file and how its rows were accounted for.</summary>
/// <param name="FileName">The file name.</param>
/// <param name="ReturnTypeCode">The return type the file holds.</param>
/// <param name="Sha256">The SHA-256 of the file's bytes.</param>
/// <param name="Totals">How its rows were accounted for.</param>
public sealed record LegacyFileSummary(string FileName, string ReturnTypeCode, string Sha256, MigrationTotals Totals);

/// <summary>A rejected or superseded legacy row.</summary>
/// <param name="FileName">The file name.</param>
/// <param name="LineNumber">The line in the file, counting the header as line 1.</param>
/// <param name="Kind">Rejected or superseded.</param>
/// <param name="Code">The stable code from <see cref="MigrationErrors"/>.</param>
/// <param name="Message">What went wrong.</param>
/// <param name="Field">The field code or legacy column, if any.</param>
/// <param name="Value">The source value at fault, if any.</param>
public sealed record LegacyRowError(
    string FileName, int LineNumber, RowErrorKind Kind, string Code, string Message, string? Field = null, string? Value = null);
