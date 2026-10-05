using RegReturns.Domain.Common;

namespace RegReturns.Domain.Templates;

/// <summary>What an administrator enters to add a validation rule. Which parameters apply depends on the rule type.</summary>
/// <param name="Code">Rule code, unique within the template.</param>
/// <param name="RuleType">The kind of rule.</param>
/// <param name="Severity">Error or warning; required and data type rules are always errors.</param>
/// <param name="TargetFieldCode">The field the finding is reported against.</param>
/// <param name="Message">The message shown when the rule fails.</param>
/// <param name="MinValue">Range: inclusive minimum.</param>
/// <param name="MaxValue">Range: inclusive maximum.</param>
/// <param name="LeftExpression">Cross-field: left expression.</param>
/// <param name="Operator">Cross-field: comparison.</param>
/// <param name="RightExpression">Cross-field: right expression.</param>
/// <param name="Tolerance">Cross-field: absolute tolerance (defaults to 0).</param>
/// <param name="ThresholdPercent">Variance: maximum change in percent.</param>
/// <param name="VarianceBasis">Variance: which earlier period to compare against.</param>
public sealed record RuleDefinition(
    string Code,
    RuleType RuleType,
    Severity Severity,
    string TargetFieldCode,
    string Message,
    decimal? MinValue = null,
    decimal? MaxValue = null,
    string? LeftExpression = null,
    ComparisonOperator? Operator = null,
    string? RightExpression = null,
    decimal? Tolerance = null,
    decimal? ThresholdPercent = null,
    VarianceBasis? VarianceBasis = null);

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

    /// <summary>Maximum number of decimal places in a rule parameter (the columns hold four).</summary>
    public const int ParameterScale = 4;

    private RuleExpression? _left;
    private RuleExpression? _right;

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

    /// <summary>Returns the field codes the rule reads: the target, plus every field its expressions refer to.</summary>
    /// <returns>The field codes.</returns>
    public IReadOnlySet<string> GetReferencedFieldCodes()
    {
        var codes = new HashSet<string>(StringComparer.Ordinal) { TargetFieldCode };
        if (RuleType == RuleType.CrossField)
        {
            codes.UnionWith(ParsedLeft().FieldCodes);
            codes.UnionWith(ParsedRight().FieldCodes);
        }

        return codes;
    }

    /// <summary>Returns the parsed left expression of a cross-field rule.</summary>
    internal RuleExpression ParsedLeft() => _left ??= ParseStored(LeftExpression);

    /// <summary>Returns the parsed right expression of a cross-field rule.</summary>
    internal RuleExpression ParsedRight() => _right ??= ParseStored(RightExpression);

    /// <summary>
    /// Creates a rule from what an administrator entered, checking every parameter. Whether the fields exist and hold
    /// numbers is checked when the rule is added to a template (<see cref="TemplateVersion.AddRule"/>).
    /// </summary>
    /// <param name="definition">The rule definition.</param>
    /// <returns>The rule, or <see cref="TemplateErrors.InvalidRule"/> or <see cref="TemplateErrors.InvalidExpression"/>.</returns>
    public static Result<ValidationRule> Create(RuleDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var code = definition.Code?.Trim();
        if (!Guard.IsCode(code, CodeMaxLength))
        {
            return Invalid($"The rule code must be 1-{CodeMaxLength} characters of A-Z, 0-9 and '_', starting with a letter.");
        }

        var target = definition.TargetFieldCode?.Trim();
        if (!Guard.IsCode(target, TemplateField.CodeMaxLength))
        {
            return Invalid("Choose the field the rule reports against.");
        }

        var message = definition.Message?.Trim();
        if (string.IsNullOrEmpty(message) || message.Length > MessageMaxLength)
        {
            return Invalid($"The message is required and must be at most {MessageMaxLength} characters.");
        }

        if (!Enum.IsDefined(definition.RuleType) || !Enum.IsDefined(definition.Severity))
        {
            return Invalid("Choose a rule type and a severity.");
        }

        var rule = new ValidationRule
        {
            Code = code!,
            RuleType = definition.RuleType,
            Severity = definition.Severity,
            TargetFieldCode = target!,
            Message = message,
            IsActive = true,
        };

        var parameters = definition.RuleType switch
        {
            RuleType.Required or RuleType.DataType => rule.SetFieldCheck(),
            RuleType.Range => rule.SetRange(definition.MinValue, definition.MaxValue),
            RuleType.CrossField => rule.SetCrossField(
                definition.LeftExpression, definition.Operator, definition.RightExpression, definition.Tolerance ?? 0m),
            _ => rule.SetVariance(definition.ThresholdPercent, definition.VarianceBasis),
        };
        return parameters.IsSuccess ? rule : parameters.Error!;
    }

    /// <summary>Creates a rule requiring the target field to have a value.</summary>
    /// <param name="code">Rule code.</param>
    /// <param name="fieldCode">Target field.</param>
    /// <param name="message">Failure message.</param>
    /// <returns>The rule.</returns>
    public static ValidationRule Required(string code, string fieldCode, string message) =>
        OrThrow(Create(new RuleDefinition(code, RuleType.Required, Severity.Error, fieldCode, message)));

    /// <summary>Creates a rule requiring the value to parse as the field's data type.</summary>
    /// <param name="code">Rule code.</param>
    /// <param name="fieldCode">Target field.</param>
    /// <param name="message">Failure message.</param>
    /// <returns>The rule.</returns>
    public static ValidationRule DataType(string code, string fieldCode, string message) =>
        OrThrow(Create(new RuleDefinition(code, RuleType.DataType, Severity.Error, fieldCode, message)));

    /// <summary>Creates a range rule.</summary>
    /// <param name="code">Rule code.</param>
    /// <param name="fieldCode">Target field.</param>
    /// <param name="severity">Severity.</param>
    /// <param name="min">Inclusive minimum, if any.</param>
    /// <param name="max">Inclusive maximum, if any.</param>
    /// <param name="message">Failure message.</param>
    /// <returns>The rule.</returns>
    public static ValidationRule Range(
        string code, string fieldCode, Severity severity, decimal? min, decimal? max, string message) =>
        OrThrow(Create(new RuleDefinition(code, RuleType.Range, severity, fieldCode, message, MinValue: min, MaxValue: max)));

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
        string message) =>
        OrThrow(Create(new RuleDefinition(
            code, RuleType.CrossField, severity, fieldCode, message,
            LeftExpression: left, Operator: comparison, RightExpression: right, Tolerance: tolerance)));

    /// <summary>Creates a period-over-period variance rule.</summary>
    /// <param name="code">Rule code.</param>
    /// <param name="fieldCode">Target field.</param>
    /// <param name="severity">Severity (usually a warning).</param>
    /// <param name="thresholdPercent">Maximum allowed absolute change in percent.</param>
    /// <param name="basis">Which earlier period to compare against.</param>
    /// <param name="message">Failure message.</param>
    /// <returns>The rule.</returns>
    public static ValidationRule Variance(
        string code, string fieldCode, Severity severity, decimal thresholdPercent, VarianceBasis basis, string message) =>
        OrThrow(Create(new RuleDefinition(
            code, RuleType.Variance, severity, fieldCode, message, ThresholdPercent: thresholdPercent, VarianceBasis: basis)));

    /// <summary>Returns this rule as a definition, for example to show or copy it.</summary>
    /// <returns>The definition.</returns>
    public RuleDefinition ToDefinition() => new(
        Code, RuleType, Severity, TargetFieldCode, Message, MinValue, MaxValue,
        LeftExpression, Operator, RightExpression, Tolerance, ThresholdPercent, VarianceBasis);

    /// <summary>Starts or stops the rule being evaluated.</summary>
    internal void SetActive(bool active) => IsActive = active;

    /// <summary>Copies the rule for a new draft version.</summary>
    internal ValidationRule Copy()
    {
        var copy = OrThrow(Create(ToDefinition()));
        copy.IsActive = IsActive;
        return copy;
    }

    private static RuleExpression ParseStored(string? text)
    {
        var parsed = RuleExpression.Parse(text);
        return parsed.IsSuccess
            ? parsed.Value
            : throw new DomainException($"A stored cross-field expression no longer parses: {parsed.Error!.Message}");
    }

    private static Error Invalid(string message) => TemplateErrors.InvalidRule.WithMessage(message);

    private static ValidationRule OrThrow(Result<ValidationRule> result) =>
        result.IsSuccess ? result.Value : throw new DomainException(result.Error!.Message);

    private static bool FitsColumn(decimal value) =>
        Math.Abs(value) <= FieldValueParser.MaxMagnitude && decimal.Round(value, ParameterScale) == value;

    private Result SetFieldCheck() =>
        Severity == Severity.Error ? Result.Success() : Invalid("Required and data type rules are always errors.");

    private Result SetRange(decimal? min, decimal? max)
    {
        if (min is null && max is null)
        {
            return Invalid("A range rule needs a minimum, a maximum or both.");
        }

        if ((min is { } low && !FitsColumn(low)) || (max is { } high && !FitsColumn(high)))
        {
            return Invalid($"Limits must have at most {ParameterScale} decimal places and 15 digits before the point.");
        }

        if (min > max)
        {
            return Invalid("The minimum cannot be greater than the maximum.");
        }

        MinValue = min;
        MaxValue = max;
        return Result.Success();
    }

    private Result SetCrossField(string? left, ComparisonOperator? comparison, string? right, decimal tolerance)
    {
        if (comparison is not { } op || !Enum.IsDefined(op))
        {
            return Invalid("Choose how the two sides compare.");
        }

        if (tolerance < 0 || !FitsColumn(tolerance))
        {
            return Invalid($"The tolerance cannot be negative and must have at most {ParameterScale} decimal places.");
        }

        var parsedLeft = RuleExpression.Parse(left);
        if (parsedLeft.IsFailure)
        {
            return parsedLeft.Error!.WithMessage("Left side: " + parsedLeft.Error.Message);
        }

        var parsedRight = RuleExpression.Parse(right);
        if (parsedRight.IsFailure)
        {
            return parsedRight.Error!.WithMessage("Right side: " + parsedRight.Error.Message);
        }

        _left = parsedLeft.Value;
        _right = parsedRight.Value;
        LeftExpression = _left.Text;
        Operator = op;
        RightExpression = _right.Text;
        Tolerance = tolerance;
        return Result.Success();
    }

    private Result SetVariance(decimal? thresholdPercent, VarianceBasis? basis)
    {
        if (thresholdPercent is not { } threshold || threshold <= 0 || !FitsColumn(threshold))
        {
            return Invalid($"The threshold must be a positive percentage with at most {ParameterScale} decimal places.");
        }

        if (basis is not { } comparedWith || !Enum.IsDefined(comparedWith))
        {
            return Invalid("Choose which earlier period to compare against.");
        }

        ThresholdPercent = threshold;
        VarianceBasis = comparedWith;
        return Result.Success();
    }
}
