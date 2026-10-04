using RegReturns.Domain.Templates;

namespace RegReturns.Infrastructure.Persistence.Seeding;

/// <summary>Small fluent helper that adds fields and rules to a draft template and fails fast on mistakes.</summary>
internal sealed class TemplateBuilder(TemplateVersion template)
{
    public TemplateVersion Template { get; } = template;

    public TemplateBuilder Amount(string code, string label, string section) =>
        Field(code, label, section, FieldDataType.Amount, "VLD m", 2);

    public TemplateBuilder Percentage(string code, string label, string section) =>
        Field(code, label, section, FieldDataType.Percentage, "%", 2);

    public TemplateBuilder Rule(ValidationRule rule)
    {
        var result = Template.AddRule(rule);
        return result.IsSuccess ? this : throw new InvalidOperationException($"{rule.Code}: {result.Error!.Message}");
    }

    /// <summary>Adds Required and DataType rules for every field, and a non-negative check for amounts.</summary>
    public TemplateBuilder StandardFieldRules(string prefix)
    {
        foreach (var field in Template.Fields.OrderBy(f => f.DisplayOrder).ToList())
        {
            Rule(ValidationRule.Required($"{prefix}_{field.Code}_REQ", field.Code, $"{field.Label} is required."));
            Rule(ValidationRule.DataType($"{prefix}_{field.Code}_TYPE", field.Code, $"{field.Label} must be a number with up to {field.Precision} decimal places."));
            Rule(field.DataType == FieldDataType.Percentage
                ? ValidationRule.Range($"{prefix}_{field.Code}_RANGE", field.Code, Severity.Error, 0m, 1000m, $"{field.Label} must be between 0% and 1000%.")
                : ValidationRule.Range($"{prefix}_{field.Code}_RANGE", field.Code, Severity.Error, 0m, null, $"{field.Label} cannot be negative."));
        }

        return this;
    }

    public TemplateVersion Publish()
    {
        var result = Template.Publish();
        return result.IsSuccess ? Template : throw new InvalidOperationException(result.Error!.Message);
    }

    private TemplateBuilder Field(string code, string label, string section, FieldDataType type, string unit, int precision)
    {
        var result = Template.AddField(code, label, section, type, unit, precision);
        return result.IsSuccess ? this : throw new InvalidOperationException($"{code}: {result.Error!.Message}");
    }
}
