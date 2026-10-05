using RegReturns.Domain.Periods;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;

namespace RegReturns.Application.Reporting;

/// <summary>A return type a report can be about.</summary>
/// <param name="Id">The return type id.</param>
/// <param name="Code">The code, such as <c>MLR</c>.</param>
/// <param name="Name">The name.</param>
/// <param name="Frequency">Monthly or quarterly.</param>
public sealed record ReportReturnType(Guid Id, string Code, string Name, ReturnFrequency Frequency);

/// <summary>What a report covers: one return type over a window of periods, for the caller's institutions.</summary>
/// <param name="ReturnType">The return type reported on.</param>
/// <param name="ReturnTypes">The return types the caller can switch to.</param>
/// <param name="Periods">The periods, oldest first.</param>
/// <param name="AsOf">The day the report is judged on (UTC).</param>
/// <param name="Institution">The caller's bank, or <see langword="null"/> when the report covers every bank.</param>
public sealed record ReportHeader(
    ReportReturnType ReturnType,
    IReadOnlyList<ReportReturnType> ReturnTypes,
    IReadOnlyList<ReportingPeriod> Periods,
    DateOnly AsOf,
    ReportInstitution? Institution)
{
    /// <summary>Gets a value indicating whether the report covers every bank (regulator staff).</summary>
    public bool AllInstitutions => Institution is null;

    /// <summary>Gets the window's label, such as <c>2025-10 to 2026-09</c>.</summary>
    public string PeriodRange => $"{Periods[0].Label} to {Periods[^1].Label}";
}

/// <summary>Filing compliance for one return type: a bank × period grid, totals and the overdue list.</summary>
/// <param name="Rows">One row per bank, in code order.</param>
/// <param name="Totals">The grid's cells counted by state.</param>
/// <param name="Overdue">Every overdue obligation of the caller's banks, of any return type and period, most overdue first.</param>
public sealed record ComplianceReport(IReadOnlyList<ComplianceRow> Rows, ComplianceTotals Totals, IReadOnlyList<OverdueItem> Overdue);

/// <summary>A bank's row in the compliance grid.</summary>
/// <param name="InstitutionCode">The bank's code.</param>
/// <param name="InstitutionName">The bank's name.</param>
/// <param name="Cells">One cell per period of the report, in the header's order.</param>
public sealed record ComplianceRow(string InstitutionCode, string InstitutionName, IReadOnlyList<ComplianceCell> Cells);

/// <summary>Where a bank stands for one period.</summary>
/// <param name="Period">The period.</param>
/// <param name="State">The compliance state.</param>
/// <param name="DueDate">The due date, if the bank had an obligation.</param>
/// <param name="FirstSubmittedAt">When it was first submitted.</param>
/// <param name="SubmissionStatus">
/// Where its return is in the workflow. Regulator staff see it only once the return has been submitted.
/// </param>
public sealed record ComplianceCell(
    ReportingPeriod Period, ComplianceState State, DateOnly? DueDate, DateTimeOffset? FirstSubmittedAt, SubmissionStatus? SubmissionStatus);

/// <summary>The cells of a compliance grid counted by state.</summary>
/// <param name="OnTime">Filed on time.</param>
/// <param name="Late">Filed late.</param>
/// <param name="Overdue">Past due with nothing on file.</param>
/// <param name="NotDue">Not filed and not due yet.</param>
public sealed record ComplianceTotals(int OnTime, int Late, int Overdue, int NotDue)
{
    /// <summary>Gets how many obligations were due.</summary>
    public int Due => OnTime + Late + Overdue;

    /// <summary>Gets the share of due obligations filed on time (0 to 1), or <see langword="null"/> when none was due.</summary>
    public decimal? OnTimeRate => Due == 0 ? null : (decimal)OnTime / Due;
}

/// <summary>An obligation past its due date with nothing on file.</summary>
/// <param name="InstitutionCode">The bank's code.</param>
/// <param name="InstitutionName">The bank's name.</param>
/// <param name="ReturnTypeCode">The return type.</param>
/// <param name="Period">The period.</param>
/// <param name="DueDate">The due date.</param>
/// <param name="DaysOverdue">Days past the due date.</param>
/// <param name="WasRejected">Whether a return was filed and rejected (otherwise none was ever submitted).</param>
public sealed record OverdueItem(
    string InstitutionCode, string InstitutionName, string ReturnTypeCode, ReportingPeriod Period, DateOnly DueDate, int DaysOverdue, bool WasRejected);

/// <summary>Validation findings of submitted returns by rule and period.</summary>
/// <param name="Rules">One line per rule and severity, most findings first.</param>
/// <param name="PerPeriod">All findings per period, in the header's order.</param>
public sealed record FindingTrend(IReadOnlyList<RuleTrend> Rules, IReadOnlyList<int> PerPeriod)
{
    /// <summary>Gets the number of findings in the window.</summary>
    public int Total => PerPeriod.Sum();
}

/// <summary>One rule's findings per period.</summary>
/// <param name="RuleCode">The rule code.</param>
/// <param name="Severity">The severity.</param>
/// <param name="Findings">Findings per period, in the header's order.</param>
/// <param name="Returns">How many returns tripped the rule in the window (a return counts once per period).</param>
public sealed record RuleTrend(string RuleCode, Severity Severity, IReadOnlyList<int> Findings, int Returns)
{
    /// <summary>Gets the rule's findings in the window.</summary>
    public int Total => Findings.Sum();
}

/// <summary>A key ratio's approved values per bank.</summary>
/// <param name="FieldCode">The field code.</param>
/// <param name="Label">The field's label in the latest published template.</param>
/// <param name="Unit">The unit, such as <c>%</c>.</param>
/// <param name="Precision">Decimal places to show.</param>
/// <param name="Series">One series per bank with an approved value in the window, in code order.</param>
public sealed record KeyRatioTrend(string FieldCode, string Label, string Unit, int Precision, IReadOnlyList<KeyRatioSeries> Series);

/// <summary>A bank's approved values of a key ratio.</summary>
/// <param name="InstitutionCode">The bank's code.</param>
/// <param name="InstitutionName">The bank's name.</param>
/// <param name="Values">The value per period, in the header's order; <see langword="null"/> where none was approved.</param>
public sealed record KeyRatioSeries(string InstitutionCode, string InstitutionName, IReadOnlyList<decimal?> Values)
{
    /// <summary>Gets the most recent approved value in the window.</summary>
    public decimal? Latest => Values.LastOrDefault(v => v is not null);

    /// <summary>Gets the lowest approved value in the window.</summary>
    public decimal? Low => Values.Min();

    /// <summary>Gets the highest approved value in the window.</summary>
    public decimal? High => Values.Max();
}

/// <summary>Everything the reports dashboard shows for one return type.</summary>
/// <param name="Header">What the dashboard covers.</param>
/// <param name="Compliance">Filing compliance.</param>
/// <param name="Findings">Validation findings of submitted returns.</param>
/// <param name="KeyRatios">Configured key ratios of the return type.</param>
public sealed record ReportsDashboard(
    ReportHeader Header, ComplianceReport Compliance, FindingTrend Findings, IReadOnlyList<KeyRatioTrend> KeyRatios);
