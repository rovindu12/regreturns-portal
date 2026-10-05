using System.Globalization;

using RegReturns.Domain.Periods;
using RegReturns.Domain.Templates;

namespace RegReturns.Web.Models.Templates;

/// <summary>Labels, badges and formatting for the template administration screens.</summary>
public static class TemplateDisplay
{
    /// <summary>The format dates are shown in, such as <c>1 Nov 2026</c>.</summary>
    public const string DateFormat = "d MMM yyyy";

    /// <summary>The format of a date input's value (what <c>&lt;input type="date"&gt;</c> posts).</summary>
    public const string DateInputFormat = "yyyy-MM-dd";

    private const string NumberFormat = "0.####";

    /// <summary>Gets every field data type, in the order the select lists them.</summary>
    public static IReadOnlyList<FieldDataType> DataTypes { get; } = Enum.GetValues<FieldDataType>();

    /// <summary>Gets every rule type, in the order the select lists them.</summary>
    public static IReadOnlyList<RuleType> RuleTypes { get; } = Enum.GetValues<RuleType>();

    /// <summary>Gets every severity.</summary>
    public static IReadOnlyList<Severity> Severities { get; } = [Severity.Error, Severity.Warning];

    /// <summary>Gets every cross-field comparison.</summary>
    public static IReadOnlyList<ComparisonOperator> Operators { get; } = Enum.GetValues<ComparisonOperator>();

    /// <summary>Gets every variance basis.</summary>
    public static IReadOnlyList<VarianceBasis> VarianceBases { get; } = Enum.GetValues<VarianceBasis>();

    /// <summary>Returns the name of a template status.</summary>
    /// <param name="status">The status.</param>
    /// <returns>The label.</returns>
    public static string StatusLabel(TemplateStatus status) => status switch
    {
        TemplateStatus.Draft => "Draft",
        TemplateStatus.Published => "Published",
        _ => "Retired",
    };

    /// <summary>Returns the Bootstrap badge classes of a template status (the badge always shows the label too).</summary>
    /// <param name="status">The status.</param>
    /// <returns>The CSS classes.</returns>
    public static string StatusBadge(TemplateStatus status) => status switch
    {
        TemplateStatus.Draft => "text-bg-warning",
        TemplateStatus.Published => "text-bg-success",
        _ => "text-bg-secondary",
    };

    /// <summary>Returns the name of a filing frequency.</summary>
    /// <param name="frequency">The frequency.</param>
    /// <returns>The label.</returns>
    public static string FrequencyLabel(ReturnFrequency frequency) =>
        frequency == ReturnFrequency.Quarterly ? "Quarterly" : "Monthly";

    /// <summary>Returns the name of a field data type.</summary>
    /// <param name="dataType">The data type.</param>
    /// <returns>The label.</returns>
    public static string DataTypeLabel(FieldDataType dataType) => dataType switch
    {
        FieldDataType.Amount => "Amount",
        FieldDataType.WholeNumber => "Whole number",
        FieldDataType.Percentage => "Percentage",
        FieldDataType.Text => "Text",
        FieldDataType.Date => "Date",
        _ => "Yes or no",
    };

    /// <summary>Returns the name of a rule type.</summary>
    /// <param name="ruleType">The rule type.</param>
    /// <returns>The label.</returns>
    public static string RuleTypeLabel(RuleType ruleType) => ruleType switch
    {
        RuleType.Required => "Required",
        RuleType.Range => "Range",
        RuleType.DataType => "Data type",
        RuleType.CrossField => "Cross-field",
        _ => "Variance",
    };

    /// <summary>Returns the name of a severity.</summary>
    /// <param name="severity">The severity.</param>
    /// <returns>The label.</returns>
    public static string SeverityLabel(Severity severity) => severity == Severity.Error ? "Error" : "Warning";

    /// <summary>Returns the Bootstrap badge classes of a severity (the badge always shows the label too).</summary>
    /// <param name="severity">The severity.</param>
    /// <returns>The CSS classes.</returns>
    public static string SeverityBadge(Severity severity) => severity == Severity.Error ? "text-bg-danger" : "text-bg-warning";

    /// <summary>Returns the symbol of a comparison, as the parameter summary shows it.</summary>
    /// <param name="comparison">The comparison.</param>
    /// <returns>The symbol.</returns>
    public static string OperatorSymbol(ComparisonOperator comparison) => comparison switch
    {
        ComparisonOperator.Equal => "=",
        ComparisonOperator.LessThanOrEqual => "≤",
        _ => "≥",
    };

    /// <summary>Returns a comparison in words, for the comparison select.</summary>
    /// <param name="comparison">The comparison.</param>
    /// <returns>The label.</returns>
    public static string OperatorLabel(ComparisonOperator comparison) => comparison switch
    {
        ComparisonOperator.Equal => "equals (=)",
        ComparisonOperator.LessThanOrEqual => "is at most (≤)",
        _ => "is at least (≥)",
    };

    /// <summary>Returns the name of a variance basis.</summary>
    /// <param name="basis">The basis.</param>
    /// <returns>The label.</returns>
    public static string BasisLabel(VarianceBasis basis) =>
        basis == VarianceBasis.SamePeriodLastYear ? "Same period last year" : "Previous period";

    /// <summary>Formats a date for reading.</summary>
    /// <param name="date">The date.</param>
    /// <returns>The date, such as <c>1 Nov 2026</c>.</returns>
    public static string Date(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    /// <summary>Formats a date as a date input's value.</summary>
    /// <param name="date">The date.</param>
    /// <returns>The date, such as <c>2026-11-01</c>.</returns>
    public static string DateInput(DateOnly date) => date.ToString(DateInputFormat, CultureInfo.InvariantCulture);

    /// <summary>Formats a rule parameter with a dot for decimals and no trailing zeros.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The number, such as <c>0.05</c>.</returns>
    public static string Number(decimal value) => value.ToString(NumberFormat, CultureInfo.InvariantCulture);

    /// <summary>Summarises the parameters of a rule in one line.</summary>
    /// <param name="rule">The rule.</param>
    /// <returns>The summary, such as <c>[L2B_HQLA] ≤ 0.15 * [TOTAL_HQLA]</c>.</returns>
    public static string Parameters(RuleDefinition rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return rule.RuleType switch
        {
            RuleType.Required => "A value is required",
            RuleType.DataType => "Must match the field's type and decimal places",
            RuleType.Range => RangeSummary(rule.MinValue, rule.MaxValue),
            RuleType.CrossField => CrossFieldSummary(rule),
            _ => VarianceSummary(rule),
        };
    }

    private static string RangeSummary(decimal? min, decimal? max) => (min, max) switch
    {
        ({ } low, { } high) => $"Between {Number(low)} and {Number(high)}",
        ({ } low, null) => $"At least {Number(low)}",
        (null, { } high) => $"At most {Number(high)}",
        _ => "No limits",
    };

    private static string CrossFieldSummary(RuleDefinition rule)
    {
        var comparison = rule.Operator is { } op ? OperatorSymbol(op) : "?";
        var summary = $"{rule.LeftExpression} {comparison} {rule.RightExpression}";
        return rule.Tolerance is { } tolerance && tolerance != 0 ? $"{summary} (tolerance {Number(tolerance)})" : summary;
    }

    private static string VarianceSummary(RuleDefinition rule)
    {
        var threshold = rule.ThresholdPercent is { } percent ? Number(percent) : "?";
        var basis = rule.VarianceBasis switch
        {
            VarianceBasis.PreviousPeriod => "the previous period",
            VarianceBasis.SamePeriodLastYear => "the same period last year",
            _ => "?",
        };
        return $"Change of at most {threshold}% against {basis}";
    }
}
