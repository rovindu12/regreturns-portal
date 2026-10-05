using System.Text.Json;

using Microsoft.AspNetCore.Mvc.ViewFeatures;

using RegReturns.Domain.Templates;

namespace RegReturns.Web.Models.Templates;

/// <summary>
/// A message carried across the redirect after a POST (Post-Redirect-Get): what was done, or why it was refused.
/// </summary>
/// <param name="IsError">Whether the change was refused.</param>
/// <param name="Text">The message.</param>
/// <param name="Section">
/// The id of the page section the message belongs to (see <see cref="TemplateSections"/>); <see langword="null"/>, or a
/// section the page does not show, puts it at the top.
/// </param>
public sealed record TemplateNotice(bool IsError, string Text, string? Section);

/// <summary>Ids of the version page's sections, used as redirect fragments and to place notices next to their form.</summary>
public static class TemplateSections
{
    /// <summary>The effective-date form.</summary>
    public const string EffectiveDate = "effective-date";

    /// <summary>The fields table.</summary>
    public const string Fields = "fields";

    /// <summary>The add-field form.</summary>
    public const string AddField = "add-field";

    /// <summary>The rules table.</summary>
    public const string Rules = "rules";

    /// <summary>The add-rule form.</summary>
    public const string AddRule = "add-rule";

    /// <summary>The publish form.</summary>
    public const string Publish = "publish";

    /// <summary>The delete-draft form.</summary>
    public const string DeleteDraft = "delete-draft";

    /// <summary>The retire form.</summary>
    public const string Retire = "retire";

    /// <summary>The start-a-new-draft form of a published version.</summary>
    public const string NewDraft = "new-draft";

    private static readonly HashSet<string> DraftSections = [EffectiveDate, Fields, AddField, Rules, AddRule, Publish, DeleteDraft];
    private static readonly HashSet<string> PublishedSections = [Retire, NewDraft];

    /// <summary>Returns whether the version page shows a section for a version in the given state.</summary>
    /// <param name="section">The section id.</param>
    /// <param name="status">The version's status.</param>
    /// <returns><see langword="true"/> if the page has the section.</returns>
    public static bool IsShown(string section, TemplateStatus status) => status switch
    {
        TemplateStatus.Draft => DraftSections.Contains(section),
        TemplateStatus.Published => PublishedSections.Contains(section),
        _ => false,
    };
}

/// <summary>Keeps template-screen notices and rejected forms in TempData (an encrypted cookie) across a redirect.</summary>
public static class TemplateTempData
{
    private const string NoticeKey = "Templates.Notice";
    private const string FieldFormKey = "Templates.FieldForm";
    private const string RuleFormKey = "Templates.RuleForm";

    /// <summary>Stores a notice for the next page.</summary>
    /// <param name="tempData">The TempData dictionary.</param>
    /// <param name="notice">The notice.</param>
    public static void SetNotice(this ITempDataDictionary tempData, TemplateNotice notice) => Put(tempData, NoticeKey, notice);

    /// <summary>Reads (and removes) the notice for this page.</summary>
    /// <param name="tempData">The TempData dictionary.</param>
    /// <returns>The notice, or <see langword="null"/>.</returns>
    public static TemplateNotice? TakeNotice(this ITempDataDictionary tempData) => Take<TemplateNotice>(tempData, NoticeKey);

    /// <summary>Stores a rejected field form so the next page shows it as typed.</summary>
    /// <param name="tempData">The TempData dictionary.</param>
    /// <param name="form">The form.</param>
    public static void SetFieldForm(this ITempDataDictionary tempData, FieldForm form) => Put(tempData, FieldFormKey, form);

    /// <summary>Reads (and removes) a rejected field form.</summary>
    /// <param name="tempData">The TempData dictionary.</param>
    /// <returns>The form, or <see langword="null"/>.</returns>
    public static FieldForm? TakeFieldForm(this ITempDataDictionary tempData) => Take<FieldForm>(tempData, FieldFormKey);

    /// <summary>Stores a rejected rule form so the next page shows it as typed.</summary>
    /// <param name="tempData">The TempData dictionary.</param>
    /// <param name="form">The form.</param>
    public static void SetRuleForm(this ITempDataDictionary tempData, RuleForm form) => Put(tempData, RuleFormKey, form);

    /// <summary>Reads (and removes) a rejected rule form.</summary>
    /// <param name="tempData">The TempData dictionary.</param>
    /// <returns>The form, or <see langword="null"/>.</returns>
    public static RuleForm? TakeRuleForm(this ITempDataDictionary tempData) => Take<RuleForm>(tempData, RuleFormKey);

    // TempData's cookie serializer only stores simple types, so objects travel as JSON text.
    private static void Put<T>(ITempDataDictionary tempData, string key, T value)
    {
        ArgumentNullException.ThrowIfNull(tempData);
        tempData[key] = JsonSerializer.Serialize(value);
    }

    private static T? Take<T>(ITempDataDictionary tempData, string key)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(tempData);
        return tempData[key] is string json ? JsonSerializer.Deserialize<T>(json) : null;
    }
}
