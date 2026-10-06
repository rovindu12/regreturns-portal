using RegReturns.Domain.Common;

namespace RegReturns.Application.Migration;

/// <summary>
/// Loads a legacy returns system's CSV exports as approved, migrated returns (ADR 0029): cleanses and maps every row,
/// validates it with the template's rules, writes the returns in one transaction, reconciles them against the source
/// and commits only a reconciled, non-dry run.
/// </summary>
public interface ILegacyMigrator
{
    /// <summary>Runs a migration.</summary>
    /// <param name="request">The source folder, mapping file and dry-run flag.</param>
    /// <param name="cancellationToken">A token to cancel the run.</param>
    /// <returns>The result, or why the run could not start (an unreadable file or an invalid mapping).</returns>
    Task<Result<LegacyMigrationResult>> RunAsync(LegacyMigrationRequest request, CancellationToken cancellationToken);
}

/// <summary>Writes a migration's error and reconciliation reports as CSV files.</summary>
public interface IMigrationReportWriter
{
    /// <summary>Writes the reports into a folder, creating it if needed and replacing earlier reports.</summary>
    /// <param name="result">The migration result.</param>
    /// <param name="directory">The folder to write into.</param>
    /// <param name="cancellationToken">A token to cancel the writes.</param>
    /// <returns>The paths written.</returns>
    Task<IReadOnlyList<string>> WriteAsync(LegacyMigrationResult result, string directory, CancellationToken cancellationToken);
}

/// <summary>What to migrate.</summary>
/// <param name="SourceDirectory">The folder holding the legacy CSV files.</param>
/// <param name="MappingPath">The JSON mapping file.</param>
/// <param name="DryRun">Whether to roll everything back after reconciling.</param>
public sealed record LegacyMigrationRequest(string SourceDirectory, string MappingPath, bool DryRun);
