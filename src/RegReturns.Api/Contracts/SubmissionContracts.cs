using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace RegReturns.Api.Contracts;

/// <summary>A return as a list shows it.</summary>
/// <param name="Id">The submission id.</param>
/// <param name="ReturnType">The return type code.</param>
/// <param name="Period">The reporting period, such as <c>2027-03</c> or <c>2027-Q1</c>.</param>
/// <param name="DueDate">The due date.</param>
/// <param name="TemplateVersion">The template version it is captured with.</param>
/// <param name="Status">
/// <c>Draft</c>, <c>Submitted</c>, <c>UnderReview</c>, <c>ReturnedForCorrection</c>, <c>Approved</c> or <c>Rejected</c>.
/// </param>
/// <param name="Revision">The revision: 1, and one more each time the regulator returns it for correction.</param>
/// <param name="Source">Where its values came from: <c>Web</c>, <c>Upload</c>, <c>Api</c> or <c>Migration</c>.</param>
/// <param name="IsLate">Whether it was first submitted after the due date.</param>
/// <param name="CreatedAt">When it was started.</param>
/// <param name="LastEditedAt">When its values last changed.</param>
/// <param name="FirstSubmittedAt">When a bank checker first submitted it to the regulator.</param>
/// <param name="DecidedAt">When the regulator approved or rejected it.</param>
/// <param name="Validation">Counts of the current revision's findings.</param>
public sealed record SubmissionResponse(
    Guid Id,
    string ReturnType,
    string Period,
    DateOnly DueDate,
    int TemplateVersion,
    string Status,
    int Revision,
    string Source,
    bool IsLate,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastEditedAt,
    DateTimeOffset? FirstSubmittedAt,
    DateTimeOffset? DecidedAt,
    ValidationSummaryResponse Validation);

/// <summary>Counts of a return's current findings.</summary>
/// <param name="IsValidated">Whether the latest values have been validated.</param>
/// <param name="Errors">Errors, which block submission.</param>
/// <param name="Warnings">Warnings.</param>
/// <param name="UnjustifiedWarnings">Warnings still waiting for a justification, which also block submission.</param>
public sealed record ValidationSummaryResponse(bool IsValidated, int Errors, int Warnings, int UnjustifiedWarnings);

/// <summary>A return with its values and history.</summary>
/// <param name="Submission">Where the return stands.</param>
/// <param name="EditVersion">The edit counter, which goes up with every change of values.</param>
/// <param name="Values">A value (or <see langword="null"/>) for every field of its template, keyed by field code.</param>
/// <param name="History">The workflow steps, oldest first.</param>
public sealed record SubmissionDetailResponse(
    SubmissionResponse Submission, int EditVersion, IReadOnlyDictionary<string, string?> Values, IReadOnlyList<WorkflowStepResponse> History);

/// <summary>One workflow step.</summary>
/// <param name="Revision">The revision it applied to.</param>
/// <param name="Action">
/// <c>Create</c>, <c>Submit</c>, <c>StartReview</c>, <c>ReturnForCorrection</c>, <c>Approve</c>, <c>Reject</c> or <c>Migrate</c>
/// (the only step of a return loaded from the legacy system).
/// </param>
/// <param name="FromStatus">The status before, or <see langword="null"/> when the draft was created.</param>
/// <param name="ToStatus">The status after.</param>
/// <param name="Comment">The comment, for example why the regulator returned it.</param>
/// <param name="OccurredAt">When.</param>
public sealed record WorkflowStepResponse(
    int Revision, string Action, string? FromStatus, string ToStatus, string? Comment, DateTimeOffset OccurredAt);

/// <summary>The validation findings of a return's current revision.</summary>
/// <param name="SubmissionId">The submission id.</param>
/// <param name="Revision">The revision the findings belong to.</param>
/// <param name="IsValidated">Whether the latest values have been validated.</param>
/// <param name="ReadyToSubmit">Whether a bank checker could submit it as it stands.</param>
/// <param name="Errors">Errors, which block submission.</param>
/// <param name="Warnings">Warnings.</param>
/// <param name="UnjustifiedWarnings">Warnings a bank user still has to justify in the portal.</param>
/// <param name="Findings">The findings, in field order, then by rule type and code.</param>
public sealed record ValidationResponse(
    Guid SubmissionId,
    int Revision,
    bool IsValidated,
    bool ReadyToSubmit,
    int Errors,
    int Warnings,
    int UnjustifiedWarnings,
    IReadOnlyList<FindingResponse> Findings);

/// <summary>A validation finding.</summary>
/// <param name="Rule">The rule code.</param>
/// <param name="Field">The field code.</param>
/// <param name="Severity"><c>Error</c> or <c>Warning</c>.</param>
/// <param name="Message">The message.</param>
/// <param name="Justification">The bank's justification of a warning, if given.</param>
public sealed record FindingResponse(string Rule, string Field, string Severity, string Message, string? Justification);

/// <summary>What a delivery did.</summary>
/// <param name="SubmissionId">The return the values went into.</param>
/// <param name="Created">Whether a new draft was started; otherwise the open return was updated.</param>
/// <param name="Changed">Whether any value changed.</param>
/// <param name="Status">The return's status.</param>
/// <param name="EditVersion">The return's edit counter.</param>
/// <param name="Validation">The findings after validation. A bank checker submits the return in the portal.</param>
public sealed record DeliveryResponse(Guid SubmissionId, bool Created, bool Changed, string Status, int EditVersion, ValidationResponse Validation);

/// <summary>A complete return for one reporting period, sent by a bank system.</summary>
public sealed class DeliverReturnRequest
{
    /// <summary>The largest number of values a request may carry.</summary>
    public const int MaxValues = 500;

    /// <summary>Gets the return type code, such as <c>MLR</c>.</summary>
    [Required]
    [RegularExpression("^[A-Za-z0-9]{1,10}$", ErrorMessage = "The return type is a code of 1 to 10 letters and digits, such as MLR.")]
    public string? ReturnType { get; init; }

    /// <summary>Gets the reporting period: a month such as <c>2027-03</c> or a quarter such as <c>2027-Q1</c>.</summary>
    [Required]
    [RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2]|Q[1-4])$", ErrorMessage = "The period is a month such as 2027-03 or a quarter such as 2027-Q1.")]
    public string? Period { get; init; }

    /// <summary>
    /// Gets every value of the return, keyed by field code: a string, a number, <c>true</c>/<c>false</c> or
    /// <see langword="null"/>. Fields left out become blank: a delivery replaces the whole return.
    /// </summary>
    [Required]
    [MaxLength(MaxValues)]
    public Dictionary<string, JsonElement>? Values { get; init; }
}

/// <summary>One page of a list.</summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Items">The items on this page.</param>
/// <param name="Page">The page number, from 1.</param>
/// <param name="PageSize">The page size.</param>
/// <param name="TotalCount">The number of items on all pages.</param>
/// <param name="TotalPages">The number of pages.</param>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);
