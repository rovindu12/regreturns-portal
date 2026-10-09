using System.Diagnostics;
using System.Globalization;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using RegReturns.Application.Abstractions;
using RegReturns.Application.Auditing;
using RegReturns.Application.Diagnostics;
using RegReturns.Application.Identity;
using RegReturns.Application.Messaging;
using RegReturns.Application.Returns;
using RegReturns.Domain.Auditing;
using RegReturns.Domain.Common;
using RegReturns.Domain.Identity;
using RegReturns.Domain.Insights;
using RegReturns.Domain.Submissions;
using RegReturns.Domain.Templates;

namespace RegReturns.Application.Insights;

/// <summary>An insight as the review page shows it.</summary>
/// <param name="Id">The insight id.</param>
/// <param name="Revision">The revision it describes.</param>
/// <param name="CreatedAt">When it was generated.</param>
/// <param name="Provider">Who wrote the narrative.</param>
/// <param name="Model">The model or rule set.</param>
/// <param name="FallbackReason">Why the rule-based writer stood in, if it did.</param>
/// <param name="Content">The narrative, movers and failed rules.</param>
/// <param name="Payload">The payload as sent (or as it would have been sent).</param>
/// <param name="InputSha256">The payload's SHA-256.</param>
/// <param name="OutputSha256">The content's SHA-256.</param>
/// <param name="DurationMs">How long it took.</param>
/// <param name="AuditSequence">The audit entry that recorded it.</param>
public sealed record ReturnInsightView(
    Guid Id,
    int Revision,
    DateTimeOffset CreatedAt,
    InsightProvider Provider,
    string Model,
    InsightFallbackReason? FallbackReason,
    InsightContent Content,
    string Payload,
    string InputSha256,
    string OutputSha256,
    int DurationMs,
    long AuditSequence)
{
    /// <summary>Builds the view of a stored insight.</summary>
    /// <param name="insight">The insight.</param>
    /// <returns>The view.</returns>
    public static ReturnInsightView Of(ReturnInsight insight)
    {
        ArgumentNullException.ThrowIfNull(insight);
        return new ReturnInsightView(
            insight.Id,
            insight.Revision,
            insight.CreatedAt,
            insight.Provider,
            insight.Model,
            insight.FallbackReason,
            InsightContent.FromJson(insight.Content),
            insight.Payload,
            insight.InputSha256,
            insight.OutputSha256,
            insight.DurationMs,
            insight.AuditSequence);
    }
}

/// <summary>Asks for the insight panel of a return: the latest insight of its current revision and who writes them.</summary>
/// <param name="SubmissionId">The return.</param>
public sealed record GetReturnInsight(Guid SubmissionId);

/// <summary>The insight panel of a return.</summary>
/// <param name="Revision">The return's current revision.</param>
/// <param name="Provider">Who writes insights, as configured.</param>
/// <param name="Model">The model or rule set configured.</param>
/// <param name="ProviderConfigured">Whether the configured provider can be called (an API key is set).</param>
/// <param name="Latest">The latest insight of the current revision, if any.</param>
public sealed record ReturnInsightPanel(
    int Revision, InsightProvider Provider, string Model, bool ProviderConfigured, ReturnInsightView? Latest);

/// <summary>Handles <see cref="GetReturnInsight"/> for supervision reviewers and approvers.</summary>
/// <param name="db">The unit of work.</param>
/// <param name="currentActor">The caller.</param>
/// <param name="narrator">The configured narrator, to describe it.</param>
public sealed class GetReturnInsightHandler(IAppDbContext db, ICurrentActor currentActor, IInsightNarrator narrator)
    : IQueryHandler<GetReturnInsight, Result<ReturnInsightPanel>>
{
    /// <inheritdoc />
    public async Task<Result<ReturnInsightPanel>> HandleAsync(GetReturnInsight query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var actor = await currentActor.GetAsync(cancellationToken);
        if (actor.IsFailure)
        {
            return actor.Error!;
        }

        if (!InsightAccess.CanUse(actor.Value))
        {
            return InsightErrors.SupervisorsOnly;
        }

        var revision = await db.Submissions.AsNoTracking()
            .VisibleTo(actor.Value)
            .Where(s => s.Id == query.SubmissionId)
            .Select(s => (int?)s.Revision)
            .SingleOrDefaultAsync(cancellationToken);
        if (revision is null)
        {
            return SubmissionErrors.NotFound;
        }

        var latest = await db.ReturnInsights.AsNoTracking()
            .Where(i => i.SubmissionId == query.SubmissionId && i.Revision == revision)
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return new ReturnInsightPanel(
            revision.Value, narrator.Provider, narrator.Model, narrator.IsConfigured, latest is null ? null : ReturnInsightView.Of(latest));
    }
}

/// <summary>
/// Generates an advisory insight on the current revision of a return for a supervisor (ADR 0030), or reuses the latest
/// one when its payload has not changed and the configured provider wrote it.
/// </summary>
/// <param name="SubmissionId">The return.</param>
public sealed record GenerateReturnInsight(Guid SubmissionId);

/// <summary>The result of <see cref="GenerateReturnInsight"/>.</summary>
/// <param name="Insight">The insight.</param>
/// <param name="Reused">Whether an earlier insight was returned because nothing changed.</param>
public sealed record InsightGeneration(ReturnInsightView Insight, bool Reused);

/// <summary>
/// Handles <see cref="GenerateReturnInsight"/>: builds the payload, guards it, asks the configured narrator and falls
/// back to the rule-based writer, records an <see cref="AuditAction.InsightGenerated"/> event, then stores the insight.
/// </summary>
/// <param name="db">The unit of work.</param>
/// <param name="currentActor">The caller.</param>
/// <param name="narrator">The configured narrator.</param>
/// <param name="auditTrail">Records the generation.</param>
/// <param name="auditContext">Who is asking, from where.</param>
/// <param name="timeProvider">The clock.</param>
/// <param name="logger">The logger.</param>
public sealed class GenerateReturnInsightHandler(
    IAppDbContext db,
    ICurrentActor currentActor,
    IInsightNarrator narrator,
    IAuditTrail auditTrail,
    IAuditContext auditContext,
    TimeProvider timeProvider,
    ILogger<GenerateReturnInsightHandler> logger) : ICommandHandler<GenerateReturnInsight, Result<InsightGeneration>>
{
    /// <summary>Outcome tag of an insight a narrator wrote as configured.</summary>
    internal const string Generated = "generated";

    /// <summary>Outcome tag of an insight the rule-based writer wrote in place of the configured provider.</summary>
    internal const string Fallback = "fallback";

    /// <summary>Outcome tag of an earlier insight returned again.</summary>
    internal const string Reused = "reused";

    /// <summary>Outcome tag of a request that was refused.</summary>
    internal const string Refused = "refused";

    /// <inheritdoc />
    public async Task<Result<InsightGeneration>> HandleAsync(GenerateReturnInsight command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var activity = RegReturnsTelemetry.ActivitySource.StartActivity("insights.generate");
        activity?.SetTag(RegReturnsTelemetry.SubmissionIdTag, command.SubmissionId);

        var actor = await currentActor.GetAsync(cancellationToken);
        if (actor.IsFailure)
        {
            return Refuse(command, actor.Error!, activity);
        }

        if (!InsightAccess.CanUse(actor.Value))
        {
            return Refuse(command, InsightErrors.SupervisorsOnly, activity);
        }

        var source = await InsightSource.LoadAsync(db, actor.Value, command.SubmissionId, cancellationToken);
        if (source is null)
        {
            return Refuse(command, SubmissionErrors.NotFound, activity);
        }

        var submission = source.Submission;
        var payload = InsightJson.Serialize(source.Request);
        var inputSha256 = ReturnInsight.Digest(payload);
        // A transient failure is worth another try, so only insights the configured provider wrote are reused, and
        // the "no API key" stand-in for as long as there is still no key.
        var configured = narrator.IsConfigured;
        var cached = await db.ReturnInsights.AsNoTracking()
            .Where(i => i.SubmissionId == submission.Id
                && i.Revision == submission.Revision
                && i.InputSha256 == inputSha256
                && ((i.Provider == narrator.Provider && i.FallbackReason == null)
                    || (!configured && i.FallbackReason == InsightFallbackReason.NotConfigured)))
            .OrderByDescending(i => i.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (cached is not null)
        {
            InsightsLog.Reused(logger, cached.Id, submission.Id, submission.Revision);
            Count(cached.Provider, Reused, null, activity);
            return new InsightGeneration(ReturnInsightView.Of(cached), Reused: true);
        }

        var started = timeProvider.GetTimestamp();
        var written = await WriteAsync(source, payload, started, cancellationToken);
        var content = InsightContent.Compose(source.Request, written.Narrative).ToJson();
        var elapsedMs = ElapsedMs(started);

        // Recorded before the insight is saved, and whatever the request's state: the payload may already have left.
        var details = Describe(source, written, inputSha256, ReturnInsight.Digest(content), elapsedMs);
        var sequence = await auditTrail.RecordAsync(
            auditContext.Current.ToRecord(AuditAction.InsightGenerated, details, nameof(Submission), submission.Id.ToString()),
            CancellationToken.None);

        var insight = ReturnInsight.Record(
            submission.Id,
            submission.Revision,
            actor.Value.UserId,
            written.Provider,
            written.Model,
            written.FallbackReason,
            payload,
            content,
            (int)Math.Min(elapsedMs, int.MaxValue),
            sequence,
            timeProvider.GetUtcNow());
        await db.ReturnInsights.AddAsync(insight, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        var outcome = written.FallbackReason is null ? Generated : Fallback;
        Count(written.Provider, outcome, written.FallbackReason, activity);
        RegReturnsTelemetry.InsightDuration.Record(
            elapsedMs,
            new KeyValuePair<string, object?>("provider", written.Provider.ToString()),
            new KeyValuePair<string, object?>(RegReturnsTelemetry.OutcomeTag, outcome));
        InsightsLog.Generated(logger, insight.Id, submission.Id, submission.Revision, written.Provider, written.Model, elapsedMs, sequence);
        return new InsightGeneration(ReturnInsightView.Of(insight), Reused: false);
    }

    private static string Describe(InsightSource source, Written written, string inputSha256, string outputSha256, long elapsedMs)
    {
        var fallback = written.FallbackReason is { } reason
            ? $" in place of {written.Attempted} ({reason})"
            : string.Empty;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Insight on {source.ReturnType.Code} {source.Request.Period} revision {source.Submission.Revision} written by {written.Provider} {written.Model}{fallback}; payload {(written.Sent ? "sent to " + written.Attempted : "not sent")}; payload sha256 {inputSha256}; content sha256 {outputSha256}; {elapsedMs} ms");
    }

    private async Task<Written> WriteAsync(InsightSource source, string payload, long started, CancellationToken cancellationToken)
    {
        var attempted = $"{narrator.Provider} {narrator.Model}";
        var guard = InsightPayloadGuard.Check(payload, source.ReturnType, source.Template);
        if (guard.IsFailure)
        {
            InsightsLog.PayloadRejected(logger, source.Submission.Id, guard.Error!.Message);
            return Written.Instead(source.Request, InsightFallbackReason.PayloadRejected, attempted, sent: false);
        }

        // Only an external provider with a key receives the payload.
        var sends = narrator.Provider != InsightProvider.RuleBased && narrator.IsConfigured;
        Result<NarratorAnswer> answer;
        try
        {
            answer = await narrator.NarrateAsync(source.Request, payload, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && sends)
        {
            var details = string.Create(
                CultureInfo.InvariantCulture,
                $"Insight on {source.ReturnType.Code} {source.Request.Period} revision {source.Submission.Revision} cancelled by the caller while {attempted} was answering; payload sent to {attempted}; payload sha256 {ReturnInsight.Digest(payload)}; {ElapsedMs(started)} ms");
            await auditTrail.RecordAsync(
                auditContext.Current.ToRecord(AuditAction.InsightGenerated, details, nameof(Submission), source.Submission.Id.ToString()),
                CancellationToken.None);
            InsightsLog.Cancelled(logger, source.Submission.Id, narrator.Provider, narrator.Model);
            throw;
        }

        if (answer.IsSuccess)
        {
            return new Written(narrator.Provider, answer.Value.Model, answer.Value.Narrative, null, attempted, sends);
        }

        var reason = InsightErrors.ReasonOf(answer.Error!);
        if (reason != InsightFallbackReason.NotConfigured)
        {
            // Running without a key is a setting, not a failure: the provider logs it once per call (5515).
            InsightsLog.FellBack(logger, narrator.Provider, narrator.Model, source.Submission.Id, answer.Error!.Code);
        }

        return Written.Instead(source.Request, reason, attempted, sends && reason != InsightFallbackReason.NotConfigured);
    }

    private long ElapsedMs(long started) => (long)timeProvider.GetElapsedTime(started).TotalMilliseconds;

    private Error Refuse(GenerateReturnInsight command, Error error, Activity? activity)
    {
        InsightsLog.Refused(logger, command.SubmissionId, error.Code);
        activity?.SetTag(RegReturnsTelemetry.OutcomeTag, Refused);
        activity?.SetTag(RegReturnsTelemetry.ErrorCodeTag, error.Code);
        return error;
    }

    private static void Count(InsightProvider provider, string outcome, InsightFallbackReason? reason, Activity? activity)
    {
        activity?.SetTag(RegReturnsTelemetry.OutcomeTag, outcome);
        activity?.SetTag("regreturns.insight.provider", provider.ToString());
        var tags = new TagList
        {
            { "provider", provider.ToString() },
            { RegReturnsTelemetry.OutcomeTag, outcome },
        };
        if (reason is { } fallbackReason)
        {
            tags.Add("fallback_reason", fallbackReason.ToString());
            activity?.SetTag("regreturns.insight.fallback_reason", fallbackReason.ToString());
        }

        RegReturnsTelemetry.InsightsGenerated.Add(1, tags);
    }

    /// <summary>A narrative and who wrote it.</summary>
    private sealed record Written(
        InsightProvider Provider, string Model, InsightNarrative Narrative, InsightFallbackReason? FallbackReason, string Attempted, bool Sent)
    {
        public static Written Instead(InsightRequest request, InsightFallbackReason reason, string attempted, bool sent) =>
            new(InsightProvider.RuleBased, RuleBasedNarrator.RuleSet, RuleBasedNarrator.Write(request), reason, attempted, sent);
    }
}

/// <summary>Who may use insights: regulator staff who review or approve returns.</summary>
internal static class InsightAccess
{
    /// <summary>Returns whether the actor may see and generate insights.</summary>
    /// <param name="actor">The caller.</param>
    /// <returns><see langword="true"/> for a regulator reviewer or approver.</returns>
    public static bool CanUse(Actor actor) =>
        actor.IsRegulatorStaff && (actor.HasRole(Role.SupervisorReviewer) || actor.HasRole(Role.SupervisorApprover));
}

/// <summary>A return with everything its payload is built from.</summary>
/// <param name="Submission">The return, with values and findings.</param>
/// <param name="ReturnType">Its return type.</param>
/// <param name="Template">Its template version, with fields and rules.</param>
/// <param name="Request">The payload.</param>
internal sealed record InsightSource(Submission Submission, ReturnType ReturnType, TemplateVersion Template, InsightRequest Request)
{
    /// <summary>Loads a return the actor may see and builds its payload.</summary>
    /// <param name="db">The unit of work.</param>
    /// <param name="actor">The caller.</param>
    /// <param name="submissionId">The return.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The source, or <see langword="null"/> when the actor cannot see the return.</returns>
    public static async Task<InsightSource?> LoadAsync(IAppDbContext db, Actor actor, Guid submissionId, CancellationToken cancellationToken)
    {
        var submission = await db.Submissions.AsNoTracking()
            .VisibleTo(actor)
            .Include(s => s.Values)
            .Include(s => s.Findings)
            .SingleOrDefaultAsync(s => s.Id == submissionId, cancellationToken);
        if (submission is null)
        {
            return null;
        }

        var template = await BankReturnAccess.LoadTemplateAsync(db, submission.TemplateVersionId, cancellationToken);
        var returnType = await db.ReturnTypes.AsNoTracking().SingleAsync(r => r.Id == submission.ReturnTypeId, cancellationToken);
        var period = await db.Obligations.AsNoTracking()
            .Where(o => o.Id == submission.ObligationId)
            .Select(o => o.Period)
            .SingleAsync(cancellationToken);
        var prior = await PriorFigures.ForAsync(db, submission, period, cancellationToken);
        return new InsightSource(submission, returnType, template, InsightPayloadBuilder.Build(returnType, template, submission, period, prior));
    }
}
