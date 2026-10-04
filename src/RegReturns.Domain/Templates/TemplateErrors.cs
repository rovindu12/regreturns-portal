using RegReturns.Domain.Common;

namespace RegReturns.Domain.Templates;

/// <summary>Business errors raised by template rules.</summary>
public static class TemplateErrors
{
    /// <summary>Only draft templates can be changed.</summary>
    public static readonly Error NotDraft = new("Template.NotDraft", "Only draft templates can be changed.");

    /// <summary>A field code is already used in the template.</summary>
    public static readonly Error DuplicateFieldCode = new(
        "Template.DuplicateFieldCode", "A field with this code already exists in the template.");

    /// <summary>A rule code is already used in the template.</summary>
    public static readonly Error DuplicateRuleCode = new(
        "Template.DuplicateRuleCode", "A rule with this code already exists in the template.");

    /// <summary>A rule targets a field that is not in the template.</summary>
    public static readonly Error UnknownField = new(
        "Template.UnknownField", "The rule refers to a field that is not in the template.");

    /// <summary>A template must have fields before it can be published.</summary>
    public static readonly Error NoFields = new(
        "Template.NoFields", "A template needs at least one field before it can be published.");
}
