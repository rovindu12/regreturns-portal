using System.Globalization;
using System.Text;

using RegReturns.Application.Migration;
using RegReturns.Infrastructure.Files;

namespace RegReturns.Infrastructure.Legacy;

/// <summary>
/// Writes a migration's reports as UTF-8 CSV files (ADR 0029): the run summary per file, the row errors, and the
/// reconciliation in detail, by bank and by period. Every cell is guarded against formula injection, since row errors
/// echo legacy values.
/// </summary>
internal sealed class MigrationReportWriter : IMigrationReportWriter
{
    /// <summary>The summary file: how each file's rows were accounted for.</summary>
    public const string SummaryFile = "summary.csv";

    /// <summary>The row error file: rejected and superseded rows.</summary>
    public const string ErrorsFile = "row-errors.csv";

    /// <summary>The detailed reconciliation: one line per field of every return.</summary>
    public const string DetailFile = "reconciliation-detail.csv";

    /// <summary>Per-field totals by return type and bank.</summary>
    public const string ByBankFile = "reconciliation-by-bank.csv";

    /// <summary>Per-field totals by return type and period.</summary>
    public const string ByPeriodFile = "reconciliation-by-period.csv";

    private const string NumberFormat = "0.##########";

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> WriteAsync(LegacyMigrationResult result, string directory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);

        var files = new (string Name, string Text)[]
        {
            (SummaryFile, Summary(result)),
            (ErrorsFile, Errors(result)),
            (DetailFile, Detail(result.Reconciliation)),
            (ByBankFile, Totals("Bank", result.Reconciliation.ByInstitution())),
            (ByPeriodFile, Totals("Period", result.Reconciliation.ByPeriod())),
        };

        var written = new List<string>(files.Length);
        foreach (var (name, text) in files)
        {
            var path = Path.Combine(directory, name);
            await File.WriteAllBytesAsync(path, CsvText.ToUtf8WithBom(text), cancellationToken);
            written.Add(path);
        }

        return written;
    }

    /// <summary>Builds the summary: one line per file and a total line, each naming the run.</summary>
    /// <param name="result">The migration result.</param>
    /// <returns>The CSV text.</returns>
    internal static string Summary(LegacyMigrationResult result)
    {
        var text = new StringBuilder();
        CsvText.AppendLine(text, ["RunId", "DryRun", "Committed", "Outcome", "File", "ReturnType", "Sha256", "RowsRead",
            "BlankRows", "SupersededRows", "RejectedRows", "MigratedReturns", "AlreadyMigratedReturns", "Mismatches"]);
        var run = result.RunId.ToString("D", CultureInfo.InvariantCulture);
        var lines = result.Files.Select(f => (f.FileName, f.ReturnTypeCode, f.Sha256, f.Totals, Mismatches: (int?)null))
            .Append(("ALL", string.Empty, string.Empty, result.Totals, result.Reconciliation.Mismatches));
        foreach (var (file, returnType, sha256, totals, mismatches) in lines)
        {
            CsvText.AppendLine(text, [run, Flag(result.IsDryRun), Flag(result.Committed), result.Outcome.ToString(), file, returnType, sha256,
                Count(totals.RowsRead), Count(totals.BlankRows), Count(totals.SupersededRows), Count(totals.RejectedRows),
                Count(totals.MigratedReturns), Count(totals.AlreadyMigratedReturns), mismatches is { } m ? Count(m) : null]);
        }

        return text.ToString();
    }

    /// <summary>Builds the row error report.</summary>
    /// <param name="result">The migration result.</param>
    /// <returns>The CSV text.</returns>
    internal static string Errors(LegacyMigrationResult result)
    {
        var text = new StringBuilder();
        CsvText.AppendLine(text, ["File", "Line", "Kind", "Code", "Column", "Value", "Message"]);
        foreach (var error in result.Errors)
        {
            CsvText.AppendLine(text, [error.FileName, Count(error.LineNumber), error.Kind.ToString(), error.Code, error.Field, error.Value, error.Message]);
        }

        return text.ToString();
    }

    /// <summary>Builds the detailed reconciliation.</summary>
    /// <param name="reconciliation">The reconciliation.</param>
    /// <returns>The CSV text.</returns>
    internal static string Detail(Reconciliation reconciliation)
    {
        var text = new StringBuilder();
        CsvText.AppendLine(text, ["ReturnType", "Bank", "Period", "Field", "Source", "Target", "Difference", "Status"]);
        foreach (var line in reconciliation.Lines)
        {
            CsvText.AppendLine(text, [line.Key.ReturnTypeCode, line.Key.InstitutionCode, line.Key.Period, line.FieldCode,
                Number(line.Source), Number(line.Target), Number(line.Difference), line.Status.ToString()]);
        }

        return text.ToString();
    }

    /// <summary>Builds a totals report.</summary>
    /// <param name="group">The name of the grouping column.</param>
    /// <param name="totals">The totals.</param>
    /// <returns>The CSV text.</returns>
    internal static string Totals(string group, IEnumerable<ReconciliationTotal> totals)
    {
        var text = new StringBuilder();
        CsvText.AppendLine(text, ["ReturnType", group, "Field", "SourceReturns", "TargetReturns", "SourceTotal", "TargetTotal", "Difference", "Status"]);
        foreach (var total in totals)
        {
            CsvText.AppendLine(text, [total.ReturnTypeCode, total.Group, total.FieldCode, Count(total.SourceReturns), Count(total.TargetReturns),
                Number(total.SourceTotal), Number(total.TargetTotal), Number(total.Difference), total.IsMatch ? "Match" : "Mismatch"]);
        }

        return text.ToString();
    }

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Flag(bool value) => value ? "true" : "false";

    private static string? Number(decimal? value) => value?.ToString(NumberFormat, CultureInfo.InvariantCulture);
}
