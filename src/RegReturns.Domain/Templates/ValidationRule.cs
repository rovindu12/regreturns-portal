using RegReturns.Domain.Common;

namespace RegReturns.Domain.Templates;

/// <summary>
/// A configurable validation rule attached to a template version.
/// Parameters are stored as typed, nullable columns; which ones apply depends on <see cref="RuleType"/>.
/// </summary>
public sealed class ValidationRule : Entity
{
    /// <summary>Maximum length of a rule code.</summary>
    public const int CodeMaxLength = 40;

    /// <summary>Maximum length of a rule message.</summary>
    public const int MessageMaxLength = 500;

    /// <summary>Maximum length of a cross-field expression.</summary>
    public const int ExpressionMaxLength = 500;

    private ValidationRule()
    {
        Code = string.Empty;
        TargetFieldCode = string.Empty;
        Message = string.Empty;
    }

    /// <summary>Gets the owning template version id.</summary>
    public Guid TemplateVersionId { get; }

    /// <summary>Gets the rule code, unique within the template (for example <c>MLR_HQLA_SUM</c>).</summary>
    public string Code { get; private set; }

    /// <summary>Gets the kind of rule.</summary>
    public RuleType RuleType { get; private set; }

    /// <summary>Gets whether a failure blocks submission.</summary>
    public Severity Severity { get; private set; }

    /// <summary>Gets the field the finding is reported against.</summary>
    public string TargetFieldCode { get; private set; }

    /// <summary>Gets the message shown when the rule fails.</summary>
    public string Message { get; private set; }

    /// <summary>Gets a value indicating whether the rule is evaluated.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Gets the inclusive minimum for <see cref="RuleType.Range"/> rules.</summary>
    public decimal? MinValue { get; private set; }

    /// <summary>Gets the inclusive maximum for <see cref="RuleType.Range"/> rules.</summary>
    public decimal? MaxValue { get; private set; }

    /// <summary>Gets the left-hand expression for <see cref="RuleType.CrossField"/> rules, for example <c>[TOTAL_HQLA]</c>.</summary>
    public string? LeftExpression { get; private set; }

    /// <summary>Gets the comparison for <see cref="RuleType.CrossField"/> rules.</summary>
    public ComparisonOperator? Operator { get; private set; }

    /// <summary>Gets the right-hand expression for <see cref="RuleType.CrossField"/> rules, for example <c>[L1] + [L2A]</c>.</summary>
    public string? RightExpression { get; private set; }

    /// <summary>Gets the absolute tolerance for <see cref="RuleType.CrossField"/> comparisons.</summary>
    public decimal? Tolerance { get; private set; }

    /// <summary>Gets the maximum allowed change in percent for <see cref="RuleType.Variance"/> rules.</summary>
    public decimal? ThresholdPercent { get; private set; }

    /// <summary>Gets the comparison period for <see cref="RuleType.Variance"/> rules.</summary>
    public VarianceBasis? VarianceBasis { get; private set; }

    /// <summary>Creates a rule requiring the target field to have a value.</summary>
    /// <param name="code">Rule code.</param>
    /// <param name="fieldCode">Target field.</param>
    /// <param name="message">Failure message.</param>
    /// <returns>The rule.</returns>
    public static ValidationRule Required(string code, string fieldCode, string message) =>
        New(code, RuleType.Required, Severity.Error, fieldCode, message);

    /// <summary>Creates a rule requiring the value to parse as the field's data type.</summary>
    /// <param name="code">Rule code.</param>
    /// <param name="fieldCode">Target field.</param>
    /// <param name="message">Failure message.</param>
    /// <returns>The rule.</returns>
    public static ValidationRule DataType(string code, string fieldCode, string message) =>
        New(code, RuleType.DataType, Severity.Error, fieldCode, message);

    /// <summary>Creates a range rule.</summary>
    /// <param name="code">Rule code.</param>
    /// <param name="fieldCode">Target field.</param>
    /// <param name="severity">Severity.</param>
    /// <param name="min">Inclusive minimum, if any.</param>
    /// <param name="max">Inclusive maximum, if any.</param>
    /// <param name="message">Failure message.</param>
    /// <returns>The rule.</returns>
    public static ValidationRule Range(
        string code, string fieldCode, Severity severity, decimal? min, decimal? max, string message)
    {
        if (min is null && max is null)
        {
            throw new DomainException("A range rule needs a minimum, a maximum or both.");
        }

        if (min > max)
        {
            throw new DomainException("A range rule's minimum cannot exceed its maximum.");
        }

        var rule = New(code, RuleType.Range, severity, fieldCode, message);
        rule.MinValue = min;
        rule.MaxValue = max;
        return rule;
    }

    /// <summary>Creates a cross-field rule comparing two expressions over field values.</summary>
    /// <param name="code">Rule code.</param>
    /// <param name="fieldCode">Field the finding is reported against.</param>
    /// <param name="severity">Severity.</param>
    /// <param name="left">Left expression, for example <c>[TOTAL_HQLA]</c>.</param>
    /// <param name="comparison">Comparison operator.</param>
    /// <param name="right">Right expression, for example <c>[L1_HQLA] + [L2A_HQLA]</c>.</param>
    /// <param name="tolerance">Absolute tolerance (non-negative).</param>
    /// <param name="message">Failure message.</param>
    /// <returns>The rule.</returns>
    public static ValidationRule CrossField(
        string code,
        string fieldCode,
        Severity severity,
        string left,
        ComparisonOperator comparison,
        string right,
        decimal tolerance,
        string message)
    {
        if (tolerance < 0)
        {
            throw new DomainException("Tolerance cannot be negative.");
        }

        var rule = New(code, RuleType.CrossField, severity, fieldCode, message);
        rule.LeftExpression = Guard.NotBlank(left, ExpressionMaxLength);
        rule.Operator = comparison;
        rule.RightExpression = Guard.NotBlank(right, ExpressionMaxLength);
        rule.Tolerance = tolerance;
        return rule;
    }

    /// <summary>Creates a period-over-period variance rule.</summary>
    /// <param name="code">Rule code.</param>
    /// <param name="fieldCode">Target field.</param>
    /// <param name="severity">Severity (usually a warning).</param>
    /// <param name="thresholdPercent">Maximum allowed absolute change in percent.</param>
    /// <param name="basis">Which earlier period to compare against.</param>
    /// <param name="message">Failure message.</param>
    /// <returns>The rule.</returns>
    public static ValidationRule Variance(
        string code, string fieldCode, Severity severity, decimal thresholdPercent, VarianceBasis basis, string message)
    {
        if (thresholdPercent <= 0)
        {
            throw new DomainException("A variance threshold must be positive.");
        }

        var rule = New(code, RuleType.Variance, severity, fieldCode, message);
        rule.ThresholdPercent = thresholdPercent;
        rule.VarianceBasis = basis;
        return rule;
    }

    /// <summary>Stops the rule being evaluated.</summary>
    public void Deactivate() => IsActive = false;

    private static ValidationRule New(string code, RuleType type, Severity severity, string fieldCode, string message) =>
        new()
        {
            Code = Guard.Code(code, CodeMaxLength),
            RuleType = type,
            Severity = severity,
            TargetFieldCode = Guard.Code(fieldCode, TemplateField.CodeMaxLength),
            Message = Guard.NotBlank(message, MessageMaxLength),
            IsActive = true,
        };
}
