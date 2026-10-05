using System.Globalization;

using RegReturns.Domain.Common;
using RegReturns.Domain.Templates;

namespace RegReturns.Web.Models.Templates;

/// <summary>
/// The add-field and edit-field forms as posted. Every value is text so the server reads numbers and choices itself
/// (<see cref="FormInput"/>) and can put the form back, as typed, when something is wrong.
/// </summary>
public sealed class FieldForm
{
    /// <summary>The decimal places a new field starts with.</summary>
    public const int DefaultPrecision = 2;

    /// <summary>Gets or sets the field code.</summary>
    public string? Code { get; set; }

    /// <summary>Gets or sets the label.</summary>
    public string? Label { get; set; }

    /// <summary>Gets or sets the section heading.</summary>
    public string? Section { get; set; }

    /// <summary>Gets or sets the data type, as a <see cref="FieldDataType"/> member name.</summary>
    public string? DataType { get; set; }

    /// <summary>Gets or sets the display unit.</summary>
    public string? Unit { get; set; }

    /// <summary>Gets or sets the decimal places.</summary>
    public string? Precision { get; set; }

    /// <summary>Returns an empty add-field form.</summary>
    /// <returns>The form.</returns>
    public static FieldForm Empty() => new()
    {
        DataType = nameof(FieldDataType.Amount),
        Precision = DefaultPrecision.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>Returns a form filled from a field definition, for editing it.</summary>
    /// <param name="definition">The field definition.</param>
    /// <returns>The form.</returns>
    public static FieldForm From(FieldDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new FieldForm
        {
            Code = definition.Code,
            Label = definition.Label,
            Section = definition.Section,
            DataType = definition.DataType.ToString(),
            Unit = definition.Unit,
            Precision = definition.Precision.ToString(CultureInfo.InvariantCulture),
        };
    }

    /// <summary>Reads the form into a field definition; the domain checks the rest when the field is saved.</summary>
    /// <returns>The definition, or <see cref="FormErrors.InvalidInput"/>.</returns>
    public Result<FieldDefinition> ToDefinition()
    {
        var dataType = FormInput.Choice<FieldDataType>(DataType, "Choose a data type.");
        if (dataType.IsFailure)
        {
            return dataType.Error!;
        }

        var precision = FormInput.WholeNumber(
            Precision, 0, $"Decimal places must be a whole number from 0 to {TemplateField.MaxPrecision}.");
        if (precision.IsFailure)
        {
            return precision.Error!;
        }

        return new FieldDefinition(
            FormInput.Code(Code), Label?.Trim() ?? string.Empty, Section?.Trim() ?? string.Empty, dataType.Value,
            Unit?.Trim() ?? string.Empty, precision.Value);
    }
}

/// <summary>
/// The add-rule form as posted. Only the parameters of the chosen rule type are read: the form shows every parameter
/// group without JavaScript, so the others may hold anything.
/// </summary>
public sealed class RuleForm
{
    /// <summary>Gets or sets the rule code.</summary>
    public string? Code { get; set; }

    /// <summary>Gets or sets the rule type, as a <see cref="Domain.Templates.RuleType"/> member name.</summary>
    public string? RuleType { get; set; }

    /// <summary>Gets or sets the severity, as a <see cref="Domain.Templates.Severity"/> member name.</summary>
    public string? Severity { get; set; }

    /// <summary>Gets or sets the code of the field the finding is reported against.</summary>
    public string? TargetFieldCode { get; set; }

    /// <summary>Gets or sets the failure message.</summary>
    public string? Message { get; set; }

    /// <summary>Gets or sets the range minimum.</summary>
    public string? MinValue { get; set; }

    /// <summary>Gets or sets the range maximum.</summary>
    public string? MaxValue { get; set; }

    /// <summary>Gets or sets the cross-field left expression.</summary>
    public string? LeftExpression { get; set; }

    /// <summary>Gets or sets the cross-field comparison, as a <see cref="ComparisonOperator"/> member name.</summary>
    public string? Operator { get; set; }

    /// <summary>Gets or sets the cross-field right expression.</summary>
    public string? RightExpression { get; set; }

    /// <summary>Gets or sets the cross-field tolerance.</summary>
    public string? Tolerance { get; set; }

    /// <summary>Gets or sets the variance threshold in percent.</summary>
    public string? ThresholdPercent { get; set; }

    /// <summary>Gets or sets the variance basis, as a <see cref="Domain.Templates.VarianceBasis"/> member name.</summary>
    public string? VarianceBasis { get; set; }

    /// <summary>Returns an empty add-rule form.</summary>
    /// <returns>The form.</returns>
    public static RuleForm Empty() => new()
    {
        RuleType = nameof(Domain.Templates.RuleType.Range),
        Severity = nameof(Domain.Templates.Severity.Error),
        Operator = nameof(ComparisonOperator.Equal),
        VarianceBasis = nameof(Domain.Templates.VarianceBasis.PreviousPeriod),
    };

    /// <summary>Reads the form into a rule definition; the domain checks the rest when the rule is added.</summary>
    /// <returns>The definition, or <see cref="FormErrors.InvalidInput"/>.</returns>
    public Result<RuleDefinition> ToDefinition()
    {
        var ruleType = FormInput.Choice<RuleType>(RuleType, "Choose a rule type.");
        if (ruleType.IsFailure)
        {
            return ruleType.Error!;
        }

        var severity = FormInput.Choice<Severity>(Severity, "Choose a severity.");
        if (severity.IsFailure)
        {
            return severity.Error!;
        }

        var rule = new RuleDefinition(
            FormInput.Code(Code), ruleType.Value, severity.Value, FormInput.Code(TargetFieldCode), Message?.Trim() ?? string.Empty);
        return ruleType.Value switch
        {
            Domain.Templates.RuleType.Range => WithRange(rule),
            Domain.Templates.RuleType.CrossField => WithCrossField(rule),
            Domain.Templates.RuleType.Variance => WithVariance(rule),
            _ => rule,
        };
    }

    private Result<RuleDefinition> WithRange(RuleDefinition rule)
    {
        var min = FormInput.OptionalDecimal(MinValue, "minimum");
        if (min.IsFailure)
        {
            return min.Error!;
        }

        var max = FormInput.OptionalDecimal(MaxValue, "maximum");
        return max.IsFailure ? max.Error! : rule with { MinValue = min.Value, MaxValue = max.Value };
    }

    private Result<RuleDefinition> WithCrossField(RuleDefinition rule)
    {
        var comparison = FormInput.OptionalChoice<ComparisonOperator>(Operator, "Choose how the two sides compare.");
        if (comparison.IsFailure)
        {
            return comparison.Error!;
        }

        var tolerance = FormInput.OptionalDecimal(Tolerance, "tolerance");
        return tolerance.IsFailure
            ? tolerance.Error!
            : rule with
            {
                LeftExpression = LeftExpression,
                Operator = comparison.Value,
                RightExpression = RightExpression,
                Tolerance = tolerance.Value,
            };
    }

    private Result<RuleDefinition> WithVariance(RuleDefinition rule)
    {
        var threshold = FormInput.OptionalDecimal(ThresholdPercent, "threshold");
        if (threshold.IsFailure)
        {
            return threshold.Error!;
        }

        var basis = FormInput.OptionalChoice<VarianceBasis>(VarianceBasis, "Choose which earlier period to compare against.");
        return basis.IsFailure ? basis.Error! : rule with { ThresholdPercent = threshold.Value, VarianceBasis = basis.Value };
    }
}
