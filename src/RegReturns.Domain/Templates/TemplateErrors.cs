using RegReturns.Domain.Common;

namespace RegReturns.Domain.Templates;

/// <summary>Business errors raised by template rules.</summary>
public static class TemplateErrors
{
    /// <summary>Only draft templates can be changed.</summary>
    public static readonly Error NotDraft = new("Template.NotDraft", "Only draft templates can be changed.");

    /// <summary>Only published templates can be retired.</summary>
    public static readonly Error NotPublished = new("Template.NotPublished", "Only a published template can be retired.");

    /// <summary>A field code is already used in the template.</summary>
    public static readonly Error DuplicateFieldCode = new(
        "Template.DuplicateFieldCode", "A field with this code already exists in the template.");

    /// <summary>A rule code is already used in the template.</summary>
    public static readonly Error DuplicateRuleCode = new(
        "Template.DuplicateRuleCode", "A rule with this code already exists in the template.");

    /// <summary>A rule targets a field that is not in the template.</summary>
    public static readonly Error UnknownField = new(
        "Template.UnknownField", "The rule refers to a field that is not in the template.");

    /// <summary>The rule to change is not in the template.</summary>
    public static readonly Error UnknownRule = new("Template.UnknownRule", "The rule is not in the template.");

    /// <summary>A template must have fields before it can be published.</summary>
    public static readonly Error NoFields = new(
        "Template.NoFields", "A template needs at least one field before it can be published.");

    /// <summary>A field definition is invalid (code, label, section, unit or precision).</summary>
    public static readonly Error InvalidField = new("Template.InvalidField", "The field definition is invalid.");

    /// <summary>A rule definition is invalid (code, message or parameters).</summary>
    public static readonly Error InvalidRule = new("Template.InvalidRule", "The rule definition is invalid.");

    /// <summary>A cross-field expression does not parse.</summary>
    public static readonly Error InvalidExpression = new(
        "Template.InvalidExpression", "The expression is not valid.");

    /// <summary>A range, cross-field or variance rule targets or refers to a field that does not hold a number.</summary>
    public static readonly Error RuleNeedsNumericField = new(
        "Template.RuleNeedsNumericField", "This kind of rule only works on number, amount and percentage fields.");

    /// <summary>A field cannot be removed or made non-numeric while rules depend on it.</summary>
    public static readonly Error FieldInUse = new(
        "Template.FieldInUse", "Rules refer to this field. Remove or change those rules first.");

    /// <summary>A return type already has a draft version.</summary>
    public static readonly Error DraftExists = new(
        "Template.DraftExists", "This return already has a draft template. Publish or delete it before starting another.");

    /// <summary>No published version applies to the reporting period.</summary>
    public static readonly Error NoApplicableVersion = new(
        "Template.NoApplicableVersion", "No published template applies to this reporting period.");

    /// <summary>The template version was not found.</summary>
    public static readonly Error NotFound = new("Template.NotFound", "The template was not found.");
}
