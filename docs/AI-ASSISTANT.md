# Advisory insights on the review page

Supervisors reviewing a return can ask for a short advisory note: the largest movements against earlier approved
returns, the validation rules the return failed, likely causes and questions to put to the bank. Claude writes the
note through the Anthropic API when an API key is configured; otherwise, or when a call fails, fixed rules write it.
The decision behind this design is [ADR 0030](adr/0030-advisory-return-insights.md).

## Using it

1. Sign in as a supervision reviewer or approver and open a submitted return from the worklist.
2. Under **Advisory insight**, press **Generate insight**. Opening the page never generates one.
3. The panel shows:
   - a badge saying who wrote it (*AI-generated, advisory* or *Rule-based, advisory*), and why fixed rules stood in
     when they did;
   - a headline, **What stands out** and **Questions for the bank**, which are the only parts a writer produces;
   - **Largest movements** (figures that moved by 10% or more, largest first) and **Failed rules**, with whether the
     bank justified each warning. The portal computes both from the figures; the model never supplies them;
   - when and for which revision it was generated, how long it took and the audit entry that recorded it;
   - **What was shared**: the exact document sent (or that would have been sent), with its SHA-256 and the SHA-256 of
     the insight.
4. **Refresh insight** generates a new one only when something changed: a new revision, or a new approved return for
   an earlier period. Otherwise the same insight is shown again and nothing is sent or recorded.

Bank staff, the auditor and administrators never see the panel. An insight never changes the return or its workflow.

## What is shared

Only codes, the regulator's own template text and figures, for one return:

| Sent | Never sent |
|---|---|
| Return type code and name, period, frequency, revision | The bank's name or code, the submission id |
| Each numeric field's code, label, section and unit | Text, date and yes/no field values |
| The current figure, the approved figures for the previous period and the same period last year, the changes in percent | People, user names, e-mail addresses |
| Each failed rule's code, type, severity, field and template text | Justification texts, workflow comments, upload names |
| Whether the bank justified each warning | Anything a bank typed |

`InsightPayloadGuard` checks every string in the document before it leaves: it must be a template code or text, a
known enumeration value or a period label, and must not contain an e-mail address. A document that fails is not sent
(log 5503) and fixed rules write the insight. A new payload member that carries other text therefore fails the guard
and its unit tests, rather than reaching a provider.

The system prompt asks for a headline, up to five observations and up to five questions, based only on the document,
in plain British English. The answer must match a JSON schema; it is trimmed to fixed lengths and stored as plain
text, which the page HTML-encodes.

## Configuration

Section `Ai` in `src/RegReturns.Web/appsettings.json`:

| Setting | Default | Meaning |
|---|---|---|
| `Ai:Provider` | `Anthropic` | `Anthropic`, or `RuleBased` to never send anything |
| `Ai:Anthropic:ApiKey` | none | Secret. Without it, fixed rules write every insight |
| `Ai:Anthropic:Model` | `claude-opus-5-5` | The model asked |
| `Ai:Anthropic:Effort` | `medium` | `low`, `medium`, `high`, `xhigh` or `max` |
| `Ai:Anthropic:MaxOutputTokens` | 16000 | Most tokens an answer may use, thinking included |
| `Ai:Anthropic:TimeoutSeconds` | 30 | How long to wait, retries included, before fixed rules answer |
| `Ai:Anthropic:MaxRetries` | 1 | Retries of a rate-limited or failed attempt within the timeout |
| `Ai:Anthropic:BaseUrl` | the SDK's | Another `https` address, such as a proxy |

Invalid settings stop the portal at start-up. To use Claude locally, put the key in `.env` and copy it into
user-secrets:

```bash
echo 'ANTHROPIC_API_KEY=<your key>' >> .env    # or edit .env; it is git-ignored and mode 600
scripts/dev-secrets.sh                          # sets Ai:Anthropic:ApiKey for the portal
```

On a server, set the environment variable `Ai__Anthropic__ApiKey`. Removing the key from `.env` and running
`scripts/dev-secrets.sh` again removes it from user-secrets.

## When fixed rules stand in

| Reason shown | Cause | Payload sent? |
|---|---|---|
| No API key is configured | `Ai:Anthropic:ApiKey` is empty | No |
| The data did not pass the privacy check | The guard rejected the document | No |
| The AI provider did not answer in time | No answer within `TimeoutSeconds` | Yes |
| The AI provider's rate limit was reached | HTTP 429 after the retries | Yes |
| The AI provider did not accept the API key | HTTP 401 or 403 | Yes |
| The AI provider rejected the request | Another 4xx, such as an unknown model | Yes |
| The AI provider could not be reached | 5xx, overload or a network failure | Yes |
| The AI model declined to answer | Stop reason `refusal` | Yes |
| The AI provider's answer could not be used | Cut short (`max_tokens`) or not matching the schema | Yes |

Fixed rules write deterministic text: the same document always gives the same insight. Their likely causes and
questions follow from the kind of rule that failed (variance, range, cross-field, required), and they point out
large movements no rule flagged. The next **Generate insight** asks the provider again, except while there is still
no key. The API's own model fallback is not used, so the audit trail always names the one model that received the
document.

## Audit and verification

Every generation records one `InsightGenerated` audit event before the insight is stored, even if storing fails, and
also when the reviewer gives up while the provider is answering. The event names the return type, period and
revision, who wrote the insight, what was attempted and why fixed rules stood in, whether the payload was sent, the
two SHA-256 digests and the duration. It never holds a figure.

To check that a stored insight is what was generated and sent:

1. On the panel, note the audit entry number and the two digests under **What was shared**.
2. As the auditor, open **Audit trail**, filter by *Insight generated*, and verify the chain.
3. The entry with that number shows the same `payload sha256` and `content sha256`.

The stored row (`returns.ReturnInsights`) keeps the payload and the content; the chain keeps their digests. A changed
row no longer matches its audit entry.

## Observability and cost

| Signal | What it carries |
|---|---|
| Logs 5501-5506 | Generated, reused, guard rejection (error), fallback, cancelled, refused; ids, codes and durations |
| Logs 5511-5515 | Provider answer (model, duration, token counts, stop reason), failure (code, HTTP status), timeout, unusable answer, no key |
| `regreturns.insights.generated` | Count by provider, outcome (`generated`, `fallback`, `reused`) and fallback reason |
| `regreturns.insights.duration` | Milliseconds per generation, by provider and outcome |
| `regreturns.ai.tokens` | Tokens by model and direction (`input`, `output`) |

Logs never carry figures, the payload, the answer or the key. A typical return is a few thousand input tokens and a
few hundred output tokens. Cost is bounded by the button (nothing runs on page views), reuse of unchanged insights and
`MaxOutputTokens`; watch `regreturns.ai.tokens` in Seq. Problems are covered in
[troubleshooting](TROUBLESHOOTING.md#12-advisory-insights).
