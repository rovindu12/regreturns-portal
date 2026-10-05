using System.Text.Json;

using RegReturns.Application.Paging;
using RegReturns.Application.Reference;
using RegReturns.Application.Returns;
using RegReturns.Application.Templates;

namespace RegReturns.Api.Contracts;

/// <summary>Maps application results to API contracts, so wire names never follow internal renames by accident.</summary>
internal static class ContractMappings
{
    /// <summary>Maps an institution.</summary>
    /// <param name="institution">The institution.</param>
    /// <returns>The API representation.</returns>
    public static InstitutionResponse ToResponse(this InstitutionReference institution) =>
        new(institution.Code, institution.Name, institution.LicenceCategory.ToString());

    /// <summary>Maps a return type.</summary>
    /// <param name="returnType">The return type.</param>
    /// <returns>The API representation.</returns>
    public static ReturnTypeResponse ToResponse(this ReturnTypeInfo returnType) => new(
        returnType.Code,
        returnType.Name,
        returnType.Description,
        returnType.Frequency.ToString(),
        returnType.DueDaysAfterPeriodEnd,
        [.. returnType.Versions.Select(v => new TemplateVersionResponse(v.Version, v.EffectiveFrom))]);

    /// <summary>Maps a template.</summary>
    /// <param name="template">The template.</param>
    /// <returns>The API representation.</returns>
    public static TemplateResponse ToResponse(this ReturnTypeTemplate template) => new(
        template.ReturnTypeCode,
        template.Version,
        template.EffectiveFrom,
        template.PeriodLabel,
        [.. template.Fields.Select(f => new FieldResponse(
            f.Code, f.Label, f.Section, f.DataType.ToString(), f.Unit, f.Precision, f.IsRequired))],
        [.. template.Rules.Select(r => new RuleResponse(r.Code, r.RuleType.ToString(), r.Severity.ToString(), r.FieldCode, r.Message))]);

    /// <summary>Maps a return in a list.</summary>
    /// <param name="summary">The return.</param>
    /// <returns>The API representation.</returns>
    public static SubmissionResponse ToResponse(this SubmissionSummary summary) => new(
        summary.Id,
        summary.ReturnTypeCode,
        summary.PeriodLabel,
        summary.DueDate,
        summary.TemplateVersion,
        summary.Status.ToString(),
        summary.Revision,
        summary.Source.ToString(),
        summary.IsLate,
        summary.CreatedAt,
        summary.LastEditedAt,
        summary.FirstSubmittedAt,
        summary.DecidedAt,
        new ValidationSummaryResponse(summary.IsValidated, summary.Errors, summary.Warnings, summary.UnjustifiedWarnings));

    /// <summary>Maps a page of returns.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The API representation.</returns>
    public static PagedResponse<SubmissionResponse> ToResponse(this PagedList<SubmissionSummary> page) =>
        new([.. page.Items.Select(ToResponse)], page.Page, page.PageSize, page.TotalCount, page.TotalPages);

    /// <summary>Maps a return with its values and history.</summary>
    /// <param name="detail">The return.</param>
    /// <returns>The API representation.</returns>
    public static SubmissionDetailResponse ToResponse(this SubmissionDetail detail) => new(
        detail.Summary.ToResponse(),
        detail.EditVersion,
        detail.Values.ToDictionary(v => v.FieldCode, v => v.Value, StringComparer.Ordinal),
        [.. detail.History.Select(s => new WorkflowStepResponse(
            s.Revision, s.Action.ToString(), s.FromStatus?.ToString(), s.ToStatus.ToString(), s.Comment, s.OccurredAt))]);

    /// <summary>Maps validation findings.</summary>
    /// <param name="validation">The findings.</param>
    /// <returns>The API representation.</returns>
    public static ValidationResponse ToResponse(this SubmissionValidation validation) => new(
        validation.SubmissionId,
        validation.Revision,
        validation.IsValidated,
        validation.IsReadyToSubmit,
        validation.Errors,
        validation.Warnings,
        validation.UnjustifiedWarnings,
        [.. validation.Findings.Select(f => new FindingResponse(f.RuleCode, f.FieldCode, f.Severity.ToString(), f.Message, f.Justification))]);

    /// <summary>Maps the outcome of a delivery.</summary>
    /// <param name="outcome">The outcome.</param>
    /// <returns>The API representation.</returns>
    public static DeliveryResponse ToResponse(this DeliveryOutcome outcome) => new(
        outcome.SubmissionId, outcome.Created, outcome.Changed, outcome.Status.ToString(), outcome.EditVersion, outcome.Validation.ToResponse());

    /// <summary>
    /// Reads delivered values as the raw text the portal would have received: strings as they are, numbers exactly as
    /// written (never through a binary floating-point value), booleans as <c>true</c>/<c>false</c>, and null as blank.
    /// </summary>
    /// <param name="values">The values by field code.</param>
    /// <param name="invalid">The codes whose value is an object or an array.</param>
    /// <returns>The raw values by field code.</returns>
    public static Dictionary<string, string?> ToRawValues(this IReadOnlyDictionary<string, JsonElement> values, out IReadOnlyList<string> invalid)
    {
        ArgumentNullException.ThrowIfNull(values);
        var raw = new Dictionary<string, string?>(values.Count, StringComparer.Ordinal);
        var rejected = new List<string>();
        foreach (var (code, value) in values)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    raw[code] = value.GetString();
                    break;
                case JsonValueKind.Number:
                    raw[code] = value.GetRawText();
                    break;
                case JsonValueKind.True or JsonValueKind.False:
                    raw[code] = value.ValueKind == JsonValueKind.True ? "true" : "false";
                    break;
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    raw[code] = null;
                    break;
                default:
                    rejected.Add(code);
                    break;
            }
        }

        invalid = rejected;
        return raw;
    }
}
