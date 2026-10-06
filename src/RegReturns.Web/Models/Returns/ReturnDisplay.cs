using System.Globalization;

using RegReturns.Application.Reporting;
using RegReturns.Application.Returns;
using RegReturns.Domain.Obligations;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;

namespace RegReturns.Web.Models.Returns;

/// <summary>Display text and Bootstrap classes for returns, so bank and supervision pages word states the same way.</summary>
public static class ReturnDisplay
{
    /// <summary>The date format used on return pages.</summary>
    public const string DateFormat = "d MMM yyyy";

    /// <summary>The date and time format used on return pages (times are shown in UTC).</summary>
    public const string DateTimeFormat = "d MMM yyyy HH:mm";

    /// <summary>Gets the label of a workflow status.</summary>
    /// <param name="status">The status.</param>
    /// <returns>The label.</returns>
    public static string Label(SubmissionStatus status) => ReportLabels.Of(status);

    /// <summary>Gets the label of an obligation's filing status.</summary>
    /// <param name="status">The status.</param>
    /// <returns>The label.</returns>
    public static string Label(ObligationStatus status) => status switch
    {
        ObligationStatus.Open => "Open",
        ObligationStatus.InProgress => "With the Bank of Valoria",
        ObligationStatus.Fulfilled => "Fulfilled",
        _ => status.ToString(),
    };

    /// <summary>Gets where a return's data came from.</summary>
    /// <param name="source">The source.</param>
    /// <returns>The label.</returns>
    public static string Label(SubmissionSource source) => source switch
    {
        SubmissionSource.Web => "Web form",
        SubmissionSource.Upload => "File upload",
        SubmissionSource.Api => "API",
        SubmissionSource.Migration => "Legacy migration",
        _ => source.ToString(),
    };

    /// <summary>Gets the past-tense label of a workflow step, as the history shows it.</summary>
    /// <param name="action">The step.</param>
    /// <returns>The label.</returns>
    public static string Label(WorkflowAction action) => action switch
    {
        WorkflowAction.Create => "Draft started",
        WorkflowAction.Submit => "Submitted",
        WorkflowAction.StartReview => "Review started",
        WorkflowAction.ReturnForCorrection => "Returned for correction",
        WorkflowAction.Approve => "Approved",
        WorkflowAction.Reject => "Rejected",
        _ => action.ToString(),
    };

    /// <summary>Formats a time in UTC, as every return page shows times.</summary>
    /// <param name="at">The time.</param>
    /// <returns>For example <c>5 Oct 2026 14:05 UTC</c>.</returns>
    public static string Utc(DateTimeOffset at) =>
        at.UtcDateTime.ToString(DateTimeFormat, CultureInfo.InvariantCulture) + " UTC";

    /// <summary>Gets the badge class of a workflow status.</summary>
    /// <param name="status">The status.</param>
    /// <returns>The Bootstrap badge classes.</returns>
    public static string BadgeClass(SubmissionStatus status) => status switch
    {
        SubmissionStatus.Draft => "text-bg-secondary",
        SubmissionStatus.Submitted => "text-bg-primary",
        SubmissionStatus.UnderReview => "text-bg-info",
        SubmissionStatus.ReturnedForCorrection => "text-bg-warning",
        SubmissionStatus.Approved => "text-bg-success",
        SubmissionStatus.Rejected => "text-bg-danger",
        _ => "text-bg-light",
    };

    /// <summary>Gets the label of an obligation without a return.</summary>
    /// <param name="row">The obligation.</param>
    /// <returns>The label.</returns>
    public static string NotStartedLabel(BankReturnRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.IsOverdue ? "Overdue, not started" : "Not started";
    }

    /// <summary>Gets the on-screen keyboard hint for a field.</summary>
    /// <param name="dataType">The field's data type.</param>
    /// <returns>The <c>inputmode</c> attribute value.</returns>
    public static string InputMode(FieldDataType dataType) => dataType switch
    {
        FieldDataType.Amount or FieldDataType.Percentage => "decimal",
        FieldDataType.WholeNumber => "numeric",
        _ => "text",
    };

    /// <summary>Describes what a field accepts, for the hint under its input.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The hint.</returns>
    public static string FormatHint(ReturnFormField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        var unit = string.IsNullOrEmpty(field.Unit) ? string.Empty : $", {field.Unit}";
        return field.DataType switch
        {
            FieldDataType.Amount or FieldDataType.Percentage when field.Precision > 0 =>
                string.Create(CultureInfo.InvariantCulture, $"Number, up to {field.Precision} decimal places{unit}"),
            FieldDataType.Amount or FieldDataType.Percentage or FieldDataType.WholeNumber => $"Whole number{unit}",
            FieldDataType.Date => "Date (YYYY-MM-DD)",
            FieldDataType.Boolean => "Yes or no",
            _ => $"Text{unit}",
        };
    }

    /// <summary>Gets whether a field's value can be shown in a date picker without losing it.</summary>
    /// <param name="value">The raw value.</param>
    /// <returns><see langword="true"/> when it is blank or a valid ISO date.</returns>
    public static bool FitsDatePicker(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        DateOnly.TryParseExact(value.Trim(), FieldValueParser.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>Summarises a validation outcome in one sentence.</summary>
    /// <param name="outcome">The outcome.</param>
    /// <returns>The sentence.</returns>
    public static string Describe(ValidationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.IsReadyToSubmit)
        {
            return outcome.Warnings == 0
                ? "Validation found no problems."
                : "Validation found no errors and every warning is justified.";
        }

        var parts = new List<string>(2);
        if (outcome.Errors > 0)
        {
            parts.Add(Count(outcome.Errors, "error"));
        }

        if (outcome.UnjustifiedWarnings > 0)
        {
            parts.Add(Count(outcome.UnjustifiedWarnings, "warning") + " to justify");
        }

        return $"Validation found {string.Join(" and ", parts)}.";
    }

    /// <summary>Formats a count with a singular or plural noun.</summary>
    /// <param name="count">The count.</param>
    /// <param name="noun">The singular noun.</param>
    /// <returns>For example <c>1 error</c> or <c>3 errors</c>.</returns>
    public static string Count(int count, string noun) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {noun}{(count == 1 ? string.Empty : "s")}");

    /// <summary>Formats a file size.</summary>
    /// <param name="bytes">The size in bytes.</param>
    /// <returns>For example <c>12.3 KB</c>.</returns>
    public static string FileSize(long bytes) => bytes switch
    {
        < 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes} bytes"),
        < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024d:0.#} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024d * 1024d):0.#} MB"),
    };
}
