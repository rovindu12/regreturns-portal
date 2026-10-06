using System.Globalization;
using System.Text.Json;

using RegReturns.Application.Reporting;
using RegReturns.Web.Models.Returns;

namespace RegReturns.Web.Models.Reports;

/// <summary>The reports dashboard page.</summary>
/// <param name="Dashboard">The dashboard, or <see langword="null"/> when it could not be built.</param>
/// <param name="Problem">Why it could not be built.</param>
public sealed record ReportsViewModel(ReportsDashboard? Dashboard, string? Problem)
{
    /// <summary>How many rules the findings chart shows on their own; the rest are summed as other rules.</summary>
    public const int ChartedRules = 6;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Gets the compliance states in legend order.</summary>
    public static IReadOnlyList<ComplianceState> Legend { get; } =
        [ComplianceState.OnTime, ComplianceState.Late, ComplianceState.Overdue, ComplianceState.NotDue, ComplianceState.NoObligation];

    /// <summary>Gets the period labels of the dashboard, oldest first.</summary>
    public IReadOnlyList<string> PeriodLabels => Dashboard is null ? [] : [.. Dashboard.Header.Periods.Select(p => p.Label)];

    /// <summary>
    /// Gets the findings chart as JSON for <c>reports.js</c>: the period labels and one stacked series per rule, the
    /// <see cref="ChartedRules"/> with most findings on their own and the rest as one.
    /// </summary>
    public string FindingChartJson
    {
        get
        {
            if (Dashboard is null)
            {
                return "{}";
            }

            var rules = Dashboard.Findings.Rules;
            var series = rules.Take(ChartedRules)
                .Select(r => new ChartSeries($"{r.RuleCode} ({r.Severity})", [.. r.Findings.Select(f => (decimal?)f)]))
                .ToList();
            if (rules.Count > ChartedRules)
            {
                var others = new decimal?[PeriodLabels.Count];
                for (var i = 0; i < others.Length; i++)
                {
                    others[i] = rules.Skip(ChartedRules).Sum(r => r.Findings[i]);
                }

                series.Add(new ChartSeries("Other rules", others));
            }

            return JsonSerializer.Serialize(new { kind = "findings", labels = PeriodLabels, series }, Json);
        }
    }

    /// <summary>Returns the CSS class of a compliance cell.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The class.</returns>
    public static string CellClass(ComplianceState state) => state switch
    {
        ComplianceState.OnTime => "compliance-on-time",
        ComplianceState.Late => "compliance-late",
        ComplianceState.Overdue => "compliance-overdue",
        ComplianceState.NotDue => "compliance-not-due",
        _ => "compliance-none",
    };

    /// <summary>Describes a cell for its tooltip: the due date, the first submission and the workflow status.</summary>
    /// <param name="bank">The bank's row.</param>
    /// <param name="cell">The cell.</param>
    /// <returns>The description.</returns>
    public static string CellTitle(ComplianceRow bank, ComplianceCell cell)
    {
        ArgumentNullException.ThrowIfNull(bank);
        ArgumentNullException.ThrowIfNull(cell);
        var parts = new List<string> { $"{bank.InstitutionCode} {cell.Period.Label}: {ReportLabels.Of(cell.State)}" };
        if (cell.DueDate is { } due)
        {
            parts.Add("due " + due.ToString(ReturnDisplay.DateFormat, CultureInfo.InvariantCulture));
        }

        if (cell.FirstSubmittedAt is { } submitted)
        {
            parts.Add("first submitted " + submitted.UtcDateTime.ToString(ReturnDisplay.DateTimeFormat, CultureInfo.InvariantCulture) + " UTC");
        }

        if (cell.SubmissionStatus is { } status)
        {
            parts.Add(ReportLabels.Of(status).ToLowerInvariant());
        }

        return string.Join(", ", parts);
    }

    /// <summary>Gets a key ratio's sparkline as JSON for <c>reports.js</c>.</summary>
    /// <param name="series">The bank's values.</param>
    /// <returns>The chart's JSON.</returns>
    public string SparklineJson(KeyRatioSeries series)
    {
        ArgumentNullException.ThrowIfNull(series);
        return JsonSerializer.Serialize(new { kind = "sparkline", labels = PeriodLabels, values = series.Values }, Json);
    }

    /// <summary>Describes a sparkline for screen readers: every period with its value.</summary>
    /// <param name="ratio">The key ratio.</param>
    /// <param name="series">The bank's values.</param>
    /// <returns>The description.</returns>
    public string SparklineLabel(KeyRatioTrend ratio, KeyRatioSeries series)
    {
        ArgumentNullException.ThrowIfNull(ratio);
        ArgumentNullException.ThrowIfNull(series);
        var points = PeriodLabels.Select((label, i) => $"{label} {FormatValue(series.Values[i], ratio.Precision, ratio.Unit)}");
        return $"{ratio.Label} of {series.InstitutionName}: {string.Join(", ", points)}";
    }

    /// <summary>Formats a value with its unit, or a dash when there is none.</summary>
    /// <param name="value">The value.</param>
    /// <param name="precision">Decimal places.</param>
    /// <param name="unit">The unit, such as <c>%</c>.</param>
    /// <returns>The text.</returns>
    public static string FormatValue(decimal? value, int precision, string unit)
    {
        if (value is not { } number)
        {
            return "-";
        }

        var text = number.ToString("N" + precision.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return unit switch
        {
            "" => text,
            "%" => text + "%",
            _ => $"{text} {unit}",
        };
    }

    /// <summary>Formats a share (0 to 1) as a percentage, or a dash when there is none.</summary>
    /// <param name="rate">The share.</param>
    /// <returns>The text.</returns>
    public static string FormatRate(decimal? rate) =>
        rate is { } value ? (value * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "-";

    /// <summary>Returns the legend text of a state: its label, after its short form when the grid shows that instead.</summary>
    /// <param name="state">The state.</param>
    /// <returns>The legend text, empty when the swatch already says it all.</returns>
    public static string LegendText(ComplianceState state) =>
        ReportLabels.ShortOf(state) == ReportLabels.Of(state) ? string.Empty : ReportLabels.Of(state);

    /// <summary>A series of the findings chart.</summary>
    /// <param name="Label">The legend label.</param>
    /// <param name="Values">The value per period.</param>
    private sealed record ChartSeries(string Label, IReadOnlyList<decimal?> Values);
}
