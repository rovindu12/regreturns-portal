using System.Globalization;

using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;

namespace RegReturns.Domain.Validation;

/// <summary>
/// Runs a template version's active rules against raw field values and returns the failures (plan §5). Pure and
/// deterministic: the same template, values and prior figures always give the same findings, in field display order,
/// whichever channel (web form, upload, API, migrator) supplied the values.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><see cref="RuleType.Required"/> fails on a blank value; <see cref="RuleType.DataType"/> on a value that does
/// not parse as the field's type and precision (<see cref="FieldValueParser"/>).</item>
/// <item><see cref="RuleType.Range"/>, <see cref="RuleType.CrossField"/> and <see cref="RuleType.Variance"/> only judge
/// values that parse: a blank or invalid value is reported once, by its own rule, not again by every rule that reads it.</item>
/// <item>Cross-field rules pass within their absolute tolerance and are skipped when either side has no value
/// (a blank field, or a division by zero).</item>
/// <item>Variance rules compare against the prior figure for the rule's basis and are skipped when there is none or it
/// is zero.</item>
/// </list>
/// </remarks>
public static class ValidationEngine
{
    /// <summary>Validates values against a template version.</summary>
    /// <param name="template">The template version the values were captured with.</param>
    /// <param name="values">Raw values keyed by field code; missing fields count as blank.</param>
    /// <param name="priorValues">
    /// Figures from the last approved return for each variance basis, keyed by field code. Leave out a basis (or pass
    /// <see langword="null"/>) when there is no such return; its variance rules are then skipped.
    /// </param>
    /// <returns>The failed rules as findings, ordered by field display order and then rule order.</returns>
    public static IReadOnlyList<FindingDraft> Validate(
        TemplateVersion template,
        IReadOnlyDictionary<string, string?> values,
        IReadOnlyDictionary<VarianceBasis, IReadOnlyDictionary<string, decimal>>? priorValues = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);

        var fields = template.Fields.ToDictionary(f => f.Code, StringComparer.Ordinal);
        var parsed = template.Fields.ToDictionary(
            f => f.Code, f => FieldValueParser.Parse(f, values.GetValueOrDefault(f.Code)), StringComparer.Ordinal);
        decimal? NumberOf(string code) => parsed.TryGetValue(code, out var value) ? value.Number : null;

        return template.Rules
            .Select((rule, index) => (Rule: rule, Index: index))
            .Where(r => r.Rule.IsActive)
            .OrderBy(r => fields[r.Rule.TargetFieldCode].DisplayOrder)
            .ThenBy(r => r.Index)
            .Select(r => Evaluate(r.Rule, parsed[r.Rule.TargetFieldCode], NumberOf, priorValues))
            .OfType<FindingDraft>()
            .ToList();
    }

    private static FindingDraft? Evaluate(
        ValidationRule rule,
        ParsedFieldValue target,
        Func<string, decimal?> numberOf,
        IReadOnlyDictionary<VarianceBasis, IReadOnlyDictionary<string, decimal>>? priorValues)
    {
        string? failure = rule.RuleType switch
        {
            RuleType.Required => target.State == FieldValueState.Blank ? string.Empty : null,
            RuleType.DataType => target.State == FieldValueState.Invalid ? string.Empty : null,
            RuleType.Range => target.Number is { } number && OutOfRange(rule, number) ? string.Empty : null,
            RuleType.CrossField => CrossFieldFailure(rule, numberOf),
            RuleType.Variance => VarianceFailure(rule, target.Number, priorValues),
            _ => throw new NotSupportedException($"Rule type {rule.RuleType} has no evaluator."),
        };

        if (failure is null)
        {
            return null;
        }

        var message = failure.Length == 0 || rule.Message.Length + failure.Length > ValidationRule.MessageMaxLength
            ? rule.Message
            : rule.Message + failure;
        return new FindingDraft(rule.Id, rule.Code, rule.TargetFieldCode, rule.Severity, message);
    }

    private static bool OutOfRange(ValidationRule rule, decimal value) =>
        (rule.MinValue is { } min && value < min) || (rule.MaxValue is { } max && value > max);

    // Returns null when the rule passes or cannot be evaluated, otherwise a detail sentence to append to the message.
    private static string? CrossFieldFailure(ValidationRule rule, Func<string, decimal?> numberOf)
    {
        var left = rule.ParsedLeft();
        var right = rule.ParsedRight();
        if (left.Evaluate(numberOf) is not { } l || right.Evaluate(numberOf) is not { } r)
        {
            return null;
        }

        var tolerance = rule.Tolerance ?? 0m;
        var passes = rule.Operator switch
        {
            ComparisonOperator.Equal => Math.Abs(l - r) <= tolerance,
            ComparisonOperator.LessThanOrEqual => l <= r + tolerance,
            ComparisonOperator.GreaterThanOrEqual => l >= r - tolerance,
            _ => throw new NotSupportedException($"Comparison {rule.Operator} has no evaluator."),
        };
        if (passes)
        {
            return null;
        }

        var leftIsTarget = string.Equals(left.Text, $"[{rule.TargetFieldCode}]", StringComparison.Ordinal);
        return leftIsTarget
            ? $" Entered {Format(l)}; calculated {Format(r)}."
            : $" Left side {Format(l)}; right side {Format(r)}.";
    }

    private static string? VarianceFailure(
        ValidationRule rule,
        decimal? current,
        IReadOnlyDictionary<VarianceBasis, IReadOnlyDictionary<string, decimal>>? priorValues)
    {
        if (current is not { } value
            || priorValues is null
            || !priorValues.TryGetValue(rule.VarianceBasis!.Value, out var prior)
            || !prior.TryGetValue(rule.TargetFieldCode, out var previous)
            || previous == 0m)
        {
            return null;
        }

        var changePercent = (value - previous) / Math.Abs(previous) * 100m;
        if (Math.Abs(changePercent) <= rule.ThresholdPercent)
        {
            return null;
        }

        var against = rule.VarianceBasis == VarianceBasis.SamePeriodLastYear ? "the same period last year" : "the previous period";
        return string.Create(
            CultureInfo.InvariantCulture,
            $" Change {changePercent:+0.00;-0.00}% against {against} (limit {rule.ThresholdPercent:0.####}%).");
    }

    private static string Format(decimal value) => value.ToString("#,0.####", CultureInfo.InvariantCulture);
}
