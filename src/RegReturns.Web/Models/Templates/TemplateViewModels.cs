using RegReturns.Application.Templates;
using RegReturns.Domain.Templates;

namespace RegReturns.Web.Models.Templates;

/// <summary>Data for the template catalogue: every return type with its versions.</summary>
/// <param name="ReturnTypes">The return types, by code, each with its versions newest first.</param>
/// <param name="DefaultEffectiveFrom">The date a new draft's form starts with: the first day of next month.</param>
/// <param name="Notice">The outcome of the last change, if any.</param>
public sealed record TemplateCatalogueViewModel(
    IReadOnlyList<ReturnTypeTemplates> ReturnTypes, DateOnly DefaultEffectiveFrom, TemplateNotice? Notice)
{
    /// <summary>Returns the newest published version of a return type, which a new draft copies.</summary>
    /// <param name="returnType">The return type.</param>
    /// <returns>The version, or <see langword="null"/> when none is published.</returns>
    public static TemplateVersionSummary? LatestPublished(ReturnTypeTemplates returnType)
    {
        ArgumentNullException.ThrowIfNull(returnType);
        return returnType.Versions.Where(v => v.Status == TemplateStatus.Published).MaxBy(v => v.Version);
    }
}

/// <summary>Data for one template version's page.</summary>
/// <param name="Template">The version with its fields and rules.</param>
/// <param name="ExistingDraft">The return type's draft when this version is not it, or <see langword="null"/>.</param>
/// <param name="DefaultEffectiveFrom">The date a new draft's form starts with: the first day of next month.</param>
/// <param name="NewField">The add-field form: empty, or as typed when it was refused.</param>
/// <param name="NewRule">The add-rule form: empty, or as typed when it was refused.</param>
/// <param name="Notice">The outcome of the last change, if any.</param>
public sealed record TemplateVersionViewModel(
    TemplateVersionDetails Template,
    TemplateVersionSummary? ExistingDraft,
    DateOnly DefaultEffectiveFrom,
    FieldForm NewField,
    RuleForm NewRule,
    TemplateNotice? Notice)
{
    /// <summary>Gets the page title, such as <c>MLR version 2</c>.</summary>
    public string Title => $"{Template.ReturnTypeCode} version {Template.Version}";

    /// <summary>Gets the notice when it belongs at the top of the page rather than next to a form.</summary>
    public TemplateNotice? PageNotice =>
        Notice is { Section: { } section } && TemplateSections.IsShown(section, Template.Status) ? null : Notice;

    /// <summary>Returns the notice when it belongs to a section of the page.</summary>
    /// <param name="section">The section id (<see cref="TemplateSections"/>).</param>
    /// <returns>The notice, or <see langword="null"/>.</returns>
    public TemplateNotice? NoticeFor(string section) =>
        Notice is not null && string.Equals(Notice.Section, section, StringComparison.Ordinal)
            && TemplateSections.IsShown(section, Template.Status)
            ? Notice
            : null;
}

/// <summary>Data for the edit-field page of a draft.</summary>
/// <param name="Template">The draft version.</param>
/// <param name="Field">The field as stored.</param>
/// <param name="Form">The form: the stored values, or as typed when the change was refused.</param>
/// <param name="Notice">The outcome of the last change, if any.</param>
public sealed record EditFieldViewModel(
    TemplateVersionDetails Template, TemplateFieldDetails Field, FieldForm Form, TemplateNotice? Notice)
{
    /// <summary>Gets the page title, such as <c>Edit TOTAL_HQLA</c>.</summary>
    public string Title => $"Edit {Field.Definition.Code}";
}

/// <summary>Data for the field inputs shared by the add-field and edit-field forms.</summary>
/// <param name="Form">The form values.</param>
/// <param name="CodeEditable">Whether the code is an input (adding) or fixed (editing).</param>
/// <param name="Template">The version, whose sections are offered as suggestions.</param>
public sealed record FieldInputsViewModel(FieldForm Form, bool CodeEditable, TemplateVersionDetails Template)
{
    /// <summary>Gets the sections already used in the version, in display order, to suggest for the section input.</summary>
    public IReadOnlyList<string> Sections =>
        [.. Template.Fields.Select(f => f.Definition.Section).Distinct(StringComparer.Ordinal)];
}
