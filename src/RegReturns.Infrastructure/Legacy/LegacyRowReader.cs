using RegReturns.Application.Migration;
using RegReturns.Domain.Common;
using RegReturns.Domain.Migration;
using RegReturns.Domain.Periods;

namespace RegReturns.Infrastructure.Legacy;

/// <summary>A cleansed legacy row, ready to load.</summary>
/// <param name="FileName">The source file.</param>
/// <param name="LineNumber">The source line.</param>
/// <param name="InstitutionCode">The bank code the row's bank name maps to.</param>
/// <param name="Period">The reporting period its reporting date ends.</param>
/// <param name="SubmittedAt">When the bank filed it in the legacy system (UTC).</param>
/// <param name="ApprovedAt">When the regulator approved it there (UTC).</param>
/// <param name="Values">The cleansed values keyed by field code, in mapping order.</param>
/// <param name="Columns">The legacy column of each field code, for messages.</param>
internal sealed record LegacyRecord(
    string FileName,
    int LineNumber,
    string InstitutionCode,
    ReportingPeriod Period,
    DateTimeOffset SubmittedAt,
    DateTimeOffset ApprovedAt,
    IReadOnlyDictionary<string, decimal?> Values,
    IReadOnlyDictionary<string, string> Columns);

/// <summary>What reading one legacy file produced.</summary>
/// <param name="Table">The file.</param>
/// <param name="Mapping">The file's mapping.</param>
/// <param name="Records">The rows to load: the last row for each bank and period, cleansed.</param>
/// <param name="Errors">The rows rejected or superseded while reading.</param>
/// <param name="BlankRows">The number of blank rows skipped.</param>
internal sealed record LegacyFileRead(
    LegacyTable Table, LegacyFileMapping Mapping, IReadOnlyList<LegacyRecord> Records, IReadOnlyList<LegacyRowError> Errors, int BlankRows);

/// <summary>
/// Turns a legacy file's rows into cleansed records (ADR 0029): it checks the header against the mapping, resolves
/// bank names and reporting dates, keeps only the last row for each bank and period (an earlier one is reported as
/// superseded), and cleanses the dates and values of the rows that remain. It never touches the database.
/// </summary>
internal static class LegacyRowReader
{
    /// <summary>Reads a file.</summary>
    /// <param name="table">The file.</param>
    /// <param name="mapping">The file's mapping.</param>
    /// <param name="frequency">The return type's frequency.</param>
    /// <param name="rules">The cleansing rules.</param>
    /// <param name="institutionLookup">Bank codes keyed by cleansed legacy name.</param>
    /// <param name="portalInstitutions">The bank codes the portal knows.</param>
    /// <returns>The records and row errors, or <see cref="MigrationErrors.SourceMismatch"/> if the header does not match the mapping.</returns>
    public static Result<LegacyFileRead> Read(
        LegacyTable table,
        LegacyFileMapping mapping,
        ReturnFrequency frequency,
        CleansingRules rules,
        IReadOnlyDictionary<string, string> institutionLookup,
        IReadOnlySet<string> portalInstitutions)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(mapping);
        var columns = MatchHeader(table, mapping);
        if (columns.IsFailure)
        {
            return columns.Error!;
        }

        var reader = new FileReader(table, mapping, columns.Value, frequency, rules, institutionLookup, portalInstitutions);
        return reader.ReadRows();
    }

    private static Result<Dictionary<string, int>> MatchHeader(LegacyTable table, LegacyFileMapping mapping)
    {
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < table.Header.Count; i++)
        {
            if (!index.TryAdd(table.Header[i], i))
            {
                return Mismatch(table, $"its header names column '{table.Header[i]}' twice");
            }
        }

        var named = mapping.Columns.Select(c => c.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (named.FirstOrDefault(c => !index.ContainsKey(c)) is { } missing)
        {
            return Mismatch(table, $"it has no column '{missing}'");
        }

        return table.Header.FirstOrDefault(h => !named.Contains(h)) is { } unmapped
            ? Mismatch(table, $"the mapping neither maps nor ignores its column '{unmapped}'")
            : index;
    }

    private static Error Mismatch(LegacyTable table, string reason) =>
        MigrationErrors.SourceMismatch.WithMessage($"{table.FileName} does not match the mapping: {reason}.");

    private sealed class FileReader(
        LegacyTable table,
        LegacyFileMapping mapping,
        Dictionary<string, int> columns,
        ReturnFrequency frequency,
        CleansingRules rules,
        IReadOnlyDictionary<string, string> institutionLookup,
        IReadOnlySet<string> portalInstitutions)
    {
        private readonly List<LegacyRowError> _errors = [];

        public LegacyFileRead ReadRows()
        {
            var blank = 0;
            var keyed = new List<(LegacyRow Row, string Institution, ReportingPeriod Period)>();
            foreach (var row in table.Rows)
            {
                if (row.IsBlank)
                {
                    blank++;
                }
                else if (Identify(row) is { } key)
                {
                    keyed.Add((row, key.Institution, key.Period));
                }
            }

            var records = new List<LegacyRecord>();
            foreach (var group in keyed.GroupBy(k => (k.Institution, k.Period)))
            {
                var winner = group.Last();
                foreach (var (row, _, _) in group.SkipLast(1))
                {
                    _errors.Add(new LegacyRowError(
                        table.FileName, row.LineNumber, RowErrorKind.Superseded, MigrationErrors.Superseded,
                        $"Replaced by line {winner.Row.LineNumber}, a later row for {winner.Institution} {mapping.ReturnType} {winner.Period.Label}; the last row wins."));
                }

                if (Cleanse(winner.Row, winner.Institution, winner.Period) is { } record)
                {
                    records.Add(record);
                }
            }

            List<LegacyRowError> errors = [.. _errors.OrderBy(e => e.LineNumber).ThenBy(e => e.Kind)];
            return new LegacyFileRead(table, mapping, records, errors, blank);
        }

        private (string Institution, ReportingPeriod Period)? Identify(LegacyRow row)
        {
            if (row.Cells.Count != table.Header.Count)
            {
                Reject(row, MigrationErrors.ColumnCount, $"The row has {row.Cells.Count} cells; the header has {table.Header.Count}.");
                return null;
            }

            var institution = Institution(row);
            var period = Period(row);
            return institution is null || period is null ? null : (institution, period);
        }

        private string? Institution(LegacyRow row)
        {
            var raw = Cell(row, mapping.InstitutionColumn);
            if (!institutionLookup.TryGetValue(LegacyCleansing.NormalizeName(raw), out var code))
            {
                Reject(row, MigrationErrors.UnknownInstitution, "The bank name is not in the mapping.", mapping.InstitutionColumn, raw);
                return null;
            }

            if (!portalInstitutions.Contains(code))
            {
                Reject(row, MigrationErrors.InstitutionNotInPortal, $"The mapping names bank {code}, which is not in the portal.", mapping.InstitutionColumn, raw);
                return null;
            }

            return code;
        }

        private ReportingPeriod? Period(LegacyRow row)
        {
            if (Date(row, mapping.PeriodEndColumn) is not { } reported)
            {
                return null;
            }

            var date = DateOnly.FromDateTime(reported);
            var period = ReportingPeriod.Containing(frequency, date);
            if (period.End != date)
            {
                var unit = frequency == ReturnFrequency.Monthly ? "month" : "quarter";
                Reject(row, MigrationErrors.NotPeriodEnd, $"The reporting date is not the last day of a {unit}.", mapping.PeriodEndColumn, Cell(row, mapping.PeriodEndColumn));
                return null;
            }

            return period;
        }

        private LegacyRecord? Cleanse(LegacyRow row, string institution, ReportingPeriod period)
        {
            var before = _errors.Count;
            var submitted = Date(row, mapping.SubmittedColumn);
            var approved = Date(row, mapping.ApprovedColumn);
            var values = new Dictionary<string, decimal?>(StringComparer.Ordinal);
            var fieldColumns = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (column, field) in mapping.Fields)
            {
                var raw = Cell(row, column);
                fieldColumns[field] = column;
                if (LegacyCleansing.TryParseNumber(raw, rules, out var value))
                {
                    values[field] = value;
                }
                else
                {
                    Reject(row, MigrationErrors.BadNumber, $"The value of {field} is not a number.", column, raw);
                }
            }

            return _errors.Count > before || submitted is null || approved is null
                ? null
                : new LegacyRecord(table.FileName, row.LineNumber, institution, period, Utc(submitted.Value), Utc(approved.Value), values, fieldColumns);
        }

        private DateTime? Date(LegacyRow row, string column)
        {
            var raw = Cell(row, column);
            if (!LegacyCleansing.TryParseDate(raw, rules, out var date))
            {
                Reject(row, MigrationErrors.BadDate, $"{column} is not a date in any of the mapping's formats.", column, raw);
                return null;
            }

            if (date is null)
            {
                Reject(row, MigrationErrors.BadDate, $"{column} is blank.", column, raw);
            }

            return date;
        }

        private string Cell(LegacyRow row, string column) => row.Cells[columns[column.Trim()]];

        private void Reject(LegacyRow row, string code, string message, string? field = null, string? value = null) =>
            _errors.Add(new LegacyRowError(table.FileName, row.LineNumber, RowErrorKind.Rejected, code, message, field, value));

        private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
