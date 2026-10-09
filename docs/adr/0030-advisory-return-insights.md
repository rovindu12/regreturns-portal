# 30. Advisory return insights: figures-only payload, Claude with a rule-based fallback, every generation audited

- Status: accepted
- Date: 2026-10-09

## Context

Phase 8 adds an assistant to the supervisors' review page (plan §6.4). A reviewer opening a return should get a short
note on what stands out: the largest movements against earlier periods, the rules that failed, likely causes and the
questions worth putting to the bank. The brief sets three constraints: send only aggregated figures and never personal
data, log every AI call to the audit trail, and keep working without the AI provider. A regulator must also be able to
say afterwards exactly what left the building, which model wrote what a reviewer read, and that nobody changed it.

## Decision

- **Supervisors ask, the page never calls on its own.** The review page shows the latest insight for the return's
  current revision. A reviewer or approver presses *Generate insight* (a `POST`), so opening a page never sends data
  out or costs money. Bank staff and other roles cannot generate or see insights.
- **A figures-only payload, built in one place.** `InsightPayloadBuilder` (pure) builds the request from the template
  and the return:
  - return type code and name, period label and frequency, revision;
  - for every numeric field (amount, whole number, percentage): code, label, section and unit, the current figure,
    the bank's approved figures for the previous period and the same period last year (the variance rules' basis),
    and the percentage changes;
  - for every finding of the current revision: rule code and type, severity, field code, the rule's text from the
    template and whether the bank justified it.

  Never sent: the bank's name or code, people, e-mail addresses, workflow comments, justification texts, upload
  names, and the values of text, date and yes/no fields. Every string in the payload is a code or text the regulator
  wrote in the template, so bank-authored text cannot reach the model: no personal data, and no prompt injection from
  a bank into the reviewer's assistant.
- **A guard before anything leaves.** `InsightPayloadGuard` walks the serialised payload and accepts a string only if
  it is a template code, template text, a known enumeration value or a period label, and contains no e-mail address. A payload that
  fails is never sent; the rule-based writer answers instead and error 5503 is logged.
- **Facts computed, narrative written.** The top movers and the failed rules shown on the panel are computed in code
  (`InsightFacts`) from the payload, never taken from the model. A narrator writes only the headline, observations
  (likely causes) and questions:
  - `AnthropicInsightNarrator` calls the Messages API through the official `Anthropic` SDK with a fixed system prompt,
    structured JSON output, model `claude-opus-5-5` and effort `medium` (both configurable). Its answer is
    validated, trimmed to fixed limits and stored as plain text, which Razor encodes when it is shown.
  - `RuleBasedNarrator` writes the same structure from fixed rules: likely causes and questions per rule type, and
    movers no rule flagged.
- **Fall back, and say so.** Without an API key, after the timeout (`Ai:Anthropic:TimeoutSeconds`, default 30, which
  covers the SDK's retries), on a provider error or rate limit, a refusal, or an answer that is not valid, the rule-based
  writer answers. The insight records the reason, the panel shows it, and the next *Generate insight* tries the
  provider again. `Ai:Provider=RuleBased` turns the provider off altogether. We rely on this application fallback
  rather than the API's server-side model fallback, so the audit trail always names the one model that received the
  payload.
- **Cached per revision and payload.** An insight is stored (`returns.ReturnInsights`) with the payload sent, its
  SHA-256, the content and its SHA-256, the provider, model, fallback reason, duration and requester. Pressing the
  button again reuses an insight of the same revision and payload that the configured provider wrote, and the "no API
  key" stand-in while there is still no key; other fallbacks are not reused, so a transient failure is tried again. A
  new revision, or a new approved prior period, changes the payload and gives a new insight.
- **One audit event per generation, recorded before the insight is saved.** `InsightGenerated` names the return and
  revision, the provider and model attempted, the outcome or fallback reason, both digests and the duration, never a
  figure. It is written even when the save that follows fails, because the payload may already have left. The insight
  row itself is `[NotAudited]`: its digests are in the chain, so a changed payload or text no longer matches its event,
  and the panel shows the audit sequence of the event that produced it.
- **Observability.** Logs 5501-5506 (use case) and 5511-5515 (provider) carry ids, codes, durations and token counts,
  never figures or text. Metrics: `regreturns.insights.generated` (provider, outcome, fallback reason),
  `regreturns.insights.duration` and `regreturns.ai.tokens` (model, direction). The SDK's HTTP calls are traced by the
  HttpClient instrumentation; the API key travels in a header, which is never logged.

## Consequences

- Reviewers get an advisory note in seconds, clearly labelled with who wrote it, for which revision, and what was
  shared; the decision stays theirs.
- The assistant cannot see justification texts, so it cannot weigh the bank's explanation. The panel shows whether
  each warning was justified, and the reviewer reads the justification next to it.
- The provider learns figures of an unnamed bank in an unnamed fictional jurisdiction. A deployment that may not share
  even that sets `Ai:Provider=RuleBased`.
- Costs are bounded by the button, the cache and `Ai:Anthropic:MaxOutputTokens`; token use is a metric.
- Adding a field type that carries bank text, or a payload member with free text, fails the guard and its unit tests
  before it can reach a provider.
