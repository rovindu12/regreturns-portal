using RegReturns.Domain.Common;

namespace RegReturns.Domain.Submissions;

/// <summary>Business errors raised by submission rules.</summary>
public static class SubmissionErrors
{
    /// <summary>The action is not allowed in the current state.</summary>
    public static readonly Error InvalidTransition = new(
        "Submission.InvalidTransition", "This action is not allowed in the submission's current state.");

    /// <summary>Values can only change in Draft or Returned for Correction.</summary>
    public static readonly Error NotEditable = new(
        "Submission.NotEditable", "The submission can only be changed while it is a draft or returned for correction.");

    /// <summary>The actor lacks the role the action needs.</summary>
    public static readonly Error RoleRequired = new(
        "Submission.RoleRequired", "You do not have the role required for this action.");

    /// <summary>The actor is not from the submitting bank.</summary>
    public static readonly Error WrongInstitution = new(
        "Submission.WrongInstitution", "Only staff of the submitting bank can do this.");

    /// <summary>The actor works for a bank, but the action is for regulator staff.</summary>
    public static readonly Error RegulatorOnly = new(
        "Submission.RegulatorOnly", "Only regulator staff can do this.");

    /// <summary>The checker prepared or last edited the return.</summary>
    public static readonly Error CheckerIsMaker = new(
        "Submission.CheckerIsMaker", "You prepared or last edited this return, so another checker must submit it.");

    /// <summary>The approver reviewed the return.</summary>
    public static readonly Error ApproverIsReviewer = new(
        "Submission.ApproverIsReviewer", "You reviewed this return, so another approver must decide it.");

    /// <summary>A comment is required.</summary>
    public static readonly Error CommentRequired = new(
        "Submission.CommentRequired", "A comment is required for this step.");

    /// <summary>The submission has no values.</summary>
    public static readonly Error NoValues = new("Submission.NoValues", "Enter or upload values before submitting.");

    /// <summary>Values changed since the last validation run.</summary>
    public static readonly Error ValidationOutdated = new(
        "Submission.ValidationOutdated", "Values changed since the last validation. Validate again before submitting.");

    /// <summary>There are unresolved validation errors.</summary>
    public static readonly Error HasErrors = new(
        "Submission.HasErrors", "Fix all validation errors before submitting.");

    /// <summary>A warning has no justification.</summary>
    public static readonly Error UnjustifiedWarnings = new(
        "Submission.UnjustifiedWarnings", "Write a justification for every warning before submitting.");

    /// <summary>The finding to justify was not found or is not a warning.</summary>
    public static readonly Error FindingNotFound = new(
        "Submission.FindingNotFound", "That warning was not found on the current revision.");

    /// <summary>The justification is too short or too long.</summary>
    public static readonly Error JustificationLength = new(
        "Submission.JustificationLength",
        $"A justification must be between {ValidationFinding.JustificationMinLength} and {ValidationFinding.JustificationMaxLength} characters.");

    /// <summary>A value refers to a field that is not in the template.</summary>
    public static readonly Error UnknownField = new(
        "Submission.UnknownField", "The value refers to a field that is not in the return template.");

    /// <summary>The template passed in is not the submission's template.</summary>
    public static readonly Error TemplateMismatch = new(
        "Submission.TemplateMismatch", "The template does not match the submission.");
}
