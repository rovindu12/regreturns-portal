using RegReturns.Domain.Periods;

namespace RegReturns.Application.Reporting;

/// <summary>A bank a report has a row for.</summary>
/// <param name="Code">The bank's code.</param>
/// <param name="Name">The bank's name.</param>
public sealed record ReportInstitution(string Code, string Name);

/// <summary>
/// Turns read model rows into report shapes. Pure functions, so the rules (what a cell shows, who sees a draft, how
/// rules are ordered) are unit tested without a database.
/// </summary>
public static class ReportShaping
{
    /// <summary>Builds the compliance grid, its totals and the overdue list.</summary>
    /// <param name="institutions">The banks to show, one row each (a bank without obligations shows empty cells).</param>
    /// <param name="periods">The periods, oldest first.</param>
    /// <param name="obligations">The return type's obligations in those periods.</param>
    /// <param name="overdue">The open obligations past their due date, of any return type and period.</param>
    /// <param name="today">The day to judge on (UTC).</param>
    /// <param name="regulatorView">
    /// Whether the caller is regulator staff, who see a return's workflow status only once it has been submitted.
    /// </param>
    /// <returns>The compliance report.</returns>
    public static ComplianceReport Compliance(
        IReadOnlyList<ReportInstitution> institutions,
        IReadOnlyList<ReportingPeriod> periods,
        IReadOnlyList<ObligationRow> obligations,
        IReadOnlyList<ObligationRow> overdue,
        DateOnly today,
        bool regulatorView)
    {
        ArgumentNullException.ThrowIfNull(institutions);
        ArgumentNullException.ThrowIfNull(periods);
        ArgumentNullException.ThrowIfNull(obligations);
        ArgumentNullException.ThrowIfNull(overdue);
        var byCell = obligations.ToDictionary(o => (o.InstitutionCode, o.Period));
        var rows = institutions
            .OrderBy(i => i.Code, StringComparer.Ordinal)
            .Select(i => new ComplianceRow(i.Code, i.Name, [.. periods.Select(p =>
                byCell.TryGetValue((i.Code, p), out var o) ? CellOf(o, today, regulatorView) : new ComplianceCell(p, ComplianceState.NoObligation, null, null, null))]))
            .ToList();

        var states = rows.SelectMany(r => r.Cells).Select(c => c.State).ToList();
        var totals = new ComplianceTotals(
            states.Count(s => s == ComplianceState.OnTime),
            states.Count(s => s == ComplianceState.Late),
            states.Count(s => s == ComplianceState.Overdue),
            states.Count(s => s == ComplianceState.NotDue));

        var overdueItems = overdue
            .Select(o => new OverdueItem(
                o.InstitutionCode,
                o.InstitutionName,
                o.ReturnTypeCode,
                o.Period,
                o.DueDate,
                Reporting.Compliance.DaysOverdue(o.DueDate, today),
                o.FirstSubmittedAt is not null))
            .OrderByDescending(o => o.DaysOverdue)
            .ThenBy(o => o.InstitutionCode, StringComparer.Ordinal)
            .ThenBy(o => o.ReturnTypeCode, StringComparer.Ordinal)
            .ToList();
        return new ComplianceReport(rows, totals, overdueItems);
    }

    /// <summary>Builds the findings trend: one line per rule and severity, most findings first.</summary>
    /// <param name="periods">The periods, oldest first.</param>
    /// <param name="counts">The finding counts.</param>
    /// <returns>The trend.</returns>
    public static FindingTrend Findings(IReadOnlyList<ReportingPeriod> periods, IReadOnlyList<FindingCountRow> counts)
    {
        ArgumentNullException.ThrowIfNull(periods);
        ArgumentNullException.ThrowIfNull(counts);
        var index = IndexOf(periods);
        var rules = counts
            .Where(c => index.ContainsKey(c.Period))
            .GroupBy(c => (c.RuleCode, c.Severity))
            .Select(g =>
            {
                var findings = new int[periods.Count];
                foreach (var count in g)
                {
                    findings[index[count.Period]] += count.Findings;
                }

                return new RuleTrend(g.Key.RuleCode, g.Key.Severity, findings, g.Sum(c => c.Returns));
            })
            .OrderByDescending(r => r.Total)
            .ThenByDescending(r => r.Severity)
            .ThenBy(r => r.RuleCode, StringComparer.Ordinal)
            .ToList();
        var perPeriod = new int[periods.Count];
        foreach (var rule in rules)
        {
            for (var i = 0; i < perPeriod.Length; i++)
            {
                perPeriod[i] += rule.Findings[i];
            }
        }

        return new FindingTrend(rules, perPeriod);
    }

    /// <summary>Builds a key ratio's series: one per bank with an approved value, values aligned to the periods.</summary>
    /// <param name="field">The field's code, label, unit and precision.</param>
    /// <param name="periods">The periods, oldest first.</param>
    /// <param name="values">The approved values of the field.</param>
    /// <returns>The trend.</returns>
    public static KeyRatioTrend KeyRatio(KeyRatioField field, IReadOnlyList<ReportingPeriod> periods, IReadOnlyList<ApprovedValueRow> values)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(periods);
        ArgumentNullException.ThrowIfNull(values);
        var index = IndexOf(periods);
        var series = values
            .Where(v => v.FieldCode == field.Code && index.ContainsKey(v.Period))
            .GroupBy(v => (v.InstitutionCode, v.InstitutionName))
            .OrderBy(g => g.Key.InstitutionCode, StringComparer.Ordinal)
            .Select(g =>
            {
                var points = new decimal?[periods.Count];
                foreach (var value in g)
                {
                    points[index[value.Period]] = value.Value;
                }

                return new KeyRatioSeries(g.Key.InstitutionCode, g.Key.InstitutionName, points);
            })
            .ToList();
        return new KeyRatioTrend(field.Code, field.Label, field.Unit, field.Precision, series);
    }

    private static ComplianceCell CellOf(ObligationRow obligation, DateOnly today, bool regulatorView) => new(
        obligation.Period,
        Reporting.Compliance.StateOf(obligation.Status, obligation.IsLate, obligation.DueDate, today),
        obligation.DueDate,
        obligation.FirstSubmittedAt,
        regulatorView && obligation.SubmissionFirstSubmittedAt is null ? null : obligation.SubmissionStatus);

    private static Dictionary<ReportingPeriod, int> IndexOf(IReadOnlyList<ReportingPeriod> periods) =>
        periods.Select((p, i) => (p, i)).ToDictionary(x => x.p, x => x.i);
}

/// <summary>The template field behind a key ratio.</summary>
/// <param name="Code">The field code.</param>
/// <param name="Label">The label.</param>
/// <param name="Unit">The unit.</param>
/// <param name="Precision">Decimal places.</param>
public sealed record KeyRatioField(string Code, string Label, string Unit, int Precision);
