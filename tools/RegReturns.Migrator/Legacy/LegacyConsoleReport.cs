using System.Globalization;

using RegReturns.Application.Migration;
using RegReturns.Domain.Migration;

namespace RegReturns.Migrator.Legacy;

/// <summary>
/// Prints a migration result as console tables: row accounting per file, returns per bank and per-field totals, source
/// against target. This is the operator's report on standard output; it is never sent to the log pipeline, which must
/// not carry return figures.
/// </summary>
internal static class LegacyConsoleReport
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>Writes the report.</summary>
    /// <param name="result">The migration result.</param>
    /// <param name="output">Where to write.</param>
    public static void Write(LegacyMigrationResult result, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(output);

        var mode = result.IsDryRun ? "dry run" : "live run";
        output.WriteLine($"Legacy migration run {result.RunId} ({mode})");
        output.WriteLine();
        output.WriteLine("Rows by file");
        var files = new ConsoleTable("File", "Type", ">Rows", ">Blank", ">Superseded", ">Rejected", ">Migrated", ">Already migrated");
        foreach (var file in result.Files)
        {
            files.Add([file.FileName, file.ReturnTypeCode, .. Counts(file.Totals)]);
        }

        files.Add(["All files", string.Empty, .. Counts(result.Totals)]);
        files.WriteTo(output);

        var reconciliation = result.Reconciliation;
        if (reconciliation.Lines.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("Returns by bank, source against target");
            var banks = new ConsoleTable("Type", "Bank", ">Source", ">Target", ">Differences", "Status");
            foreach (var count in reconciliation.ReturnsByInstitution())
            {
                banks.Add(count.ReturnTypeCode, count.Group, Number(count.SourceReturns), Number(count.TargetReturns),
                    Number(count.Mismatches), count.IsMatch ? "Match" : "MISMATCH");
            }

            banks.WriteTo(output);
            output.WriteLine();
            output.WriteLine("Field totals over every bank and period, source against target");
            var fields = new ConsoleTable("Type", "Field", ">Returns", ">Source total", ">Target total", ">Difference", "Status");
            foreach (var total in reconciliation.ByField())
            {
                fields.Add(total.ReturnTypeCode, total.FieldCode, Number(total.SourceReturns), Amount(total.SourceTotal),
                    Amount(total.TargetTotal), Amount(total.Difference), total.IsMatch ? "Match" : "MISMATCH");
            }

            fields.WriteTo(output);
        }

        output.WriteLine();
        output.WriteLine(Conclusion(result));
    }

    /// <summary>Says how the run ended and what happened to the data, in one sentence.</summary>
    /// <param name="result">The migration result.</param>
    /// <returns>The sentence.</returns>
    internal static string Conclusion(LegacyMigrationResult result)
    {
        var totals = result.Totals;
        var excluded = totals.RejectedRows + totals.SupersededRows > 0
            ? $" {totals.RejectedRows} row(s) rejected and {totals.SupersededRows} superseded; see row-errors.csv in the report folder."
            : string.Empty;
        if (result.Outcome == MigrationOutcome.Mismatch)
        {
            return $"MISMATCH: {result.Reconciliation.Mismatches} difference(s) between source and target"
                + (totals.IsBalanced ? string.Empty : ", and the rows do not add up")
                + ". Nothing was committed." + excluded;
        }

        return result.Committed
            ? $"Reconciled and committed: {totals.MigratedReturns} return(s) migrated, {totals.AlreadyMigratedReturns} already migrated." + excluded
            : $"Reconciled: {totals.MigratedReturns} return(s) would be migrated, {totals.AlreadyMigratedReturns} already migrated. Dry run, so everything was rolled back." + excluded;
    }

    private static string[] Counts(MigrationTotals totals) =>
    [
        Number(totals.RowsRead), Number(totals.BlankRows), Number(totals.SupersededRows), Number(totals.RejectedRows),
        Number(totals.MigratedReturns), Number(totals.AlreadyMigratedReturns),
    ];

    private static string Number(int value) => value.ToString("N0", Invariant);

    private static string Amount(decimal value) => value.ToString("#,##0.00", Invariant);
}
