using System.Diagnostics;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Diagnostics;
using RegReturns.Domain.Common;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;
using RegReturns.Domain.Validation;

namespace RegReturns.Application.Returns;

/// <summary>Counts of the current revision's findings after a validation run.</summary>
/// <param name="Errors">Errors, which block submission.</param>
/// <param name="Warnings">Warnings.</param>
/// <param name="UnjustifiedWarnings">Warnings still waiting for a justification.</param>
public sealed record ValidationOutcome(int Errors, int Warnings, int UnjustifiedWarnings)
{
    /// <summary>Gets a value indicating whether nothing blocks submission.</summary>
    public bool IsReadyToSubmit => Errors == 0 && UnjustifiedWarnings == 0;

    /// <summary>Counts a submission's current findings.</summary>
    /// <param name="submission">The submission.</param>
    /// <returns>The counts.</returns>
    public static ValidationOutcome Of(Submission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        var findings = submission.CurrentFindings.ToList();
        return new ValidationOutcome(
            findings.Count(f => f.Severity == Severity.Error),
            findings.Count(f => f.Severity == Severity.Warning),
            findings.Count(f => f.Severity == Severity.Warning && f.BlocksSubmission));
    }
}

/// <summary>
/// Runs the validation engine on a submission (plan §5): the values against its pinned template version, with variance
/// rules compared to the bank's last approved return for the previous period and for the same period last year.
/// </summary>
/// <param name="db">The unit of work.</param>
/// <param name="logger">The logger.</param>
public sealed class ReturnValidator(IAppDbContext db, ILogger<ReturnValidator> logger)
{
    /// <summary>Validates the submission and records the findings on it; the caller saves.</summary>
    /// <param name="submission">The submission, with values and findings loaded and tracked.</param>
    /// <param name="template">The submission's template version, with fields and rules.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The finding counts, or the rule that was broken (the submission is no longer editable).</returns>
    public async Task<Result<ValidationOutcome>> ValidateAsync(
        Submission submission, TemplateVersion template, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(template);
        using var activity = RegReturnsTelemetry.ActivitySource.StartActivity("returns.validate");
        activity?.SetTag("regreturns.submission_id", submission.Id.ToString());
        activity?.SetTag("regreturns.template_version_id", template.Id.ToString());

        var period = await db.Obligations.AsNoTracking()
            .Where(o => o.Id == submission.ObligationId)
            .Select(o => o.Period)
            .SingleAsync(cancellationToken);
        var prior = await PriorFigures.ForAsync(db, submission, period, cancellationToken);

        var values = submission.Values.ToDictionary(v => v.FieldCode, v => v.RawValue, StringComparer.Ordinal);
        var findings = ValidationEngine.Validate(template, values, prior);
        var recorded = submission.RecordValidation(findings);
        if (recorded.IsFailure)
        {
            return recorded.Error!;
        }

        var outcome = ValidationOutcome.Of(submission);
        Record(submission, findings, outcome, activity);
        return outcome;
    }

    private void Record(Submission submission, IReadOnlyList<FindingDraft> findings, ValidationOutcome outcome, Activity? activity)
    {
        var result = "clean";
        if (outcome.Errors > 0)
        {
            result = "errors";
        }
        else if (outcome.Warnings > 0)
        {
            result = "warnings";
        }

        activity?.SetTag("regreturns.validation.outcome", result);
        RegReturnsTelemetry.ValidationRuns.Add(1, new KeyValuePair<string, object?>("outcome", result));
        foreach (var finding in findings)
        {
            RegReturnsTelemetry.ValidationFindings.Add(
                1,
                new KeyValuePair<string, object?>("rule_code", finding.RuleCode),
                new KeyValuePair<string, object?>("severity", finding.Severity.ToString()));
        }

        ReturnsLog.Validated(logger, submission.Id, submission.Revision, outcome.Errors, outcome.Warnings, outcome.UnjustifiedWarnings);
    }
}
