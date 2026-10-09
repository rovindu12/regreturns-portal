# Changelog

All notable changes are recorded here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Phase 1: Clean Architecture solution on .NET 10 with central package management, lock files and strict analyzers.
- Domain model for institutions, users, return types, versioned templates, validation rules, obligations and submissions,
  with the maker-checker and supervisory workflow and segregation-of-duties rules.
- EF Core 10 model, initial migration and SQL Server schemas (`reference`, `iam`, `returns`).
- Deterministic demo data: 5 fictional banks, 3 return types, 12 months of history with planted anomalies.
- Migrator console (`migrate-db [--seed]`, `seed`).
- Observability baseline: Serilog JSON logs, OpenTelemetry traces and metrics, Seq, W3C trace id on every response,
  sensitive-data redaction, `/health/live` and `/health/ready`.
- Unit, architecture and Testcontainers integration tests; CI with format check, coverage gate and vulnerable-package check.

- Phase 2: WSO2 Identity Server 7.3 in Docker Compose, persisted on the shared SQL Server (ADR 0014), with a local
  development CA that the apps trust explicitly and WSO2's default keys and admin credentials replaced (ADR 0015).
- IamBootstrap console (`apply`, `demo-users`): creates WSO2 claims, the RegReturns API resource and scopes, the portal
  application with PKCE and an adaptive MFA script, application roles, one machine client per bank and the demo users.
  Idempotent; generated client secrets go to a git-ignored `.env.generated` (ADR 0017).
- IamBootstrap also enrols TOTP as the user for every demo user who reaches the TOTP step, turns off WSO2
  self-registration, account recovery, TOTP enrolment during sign-in and My Account, and creates the SCIM provisioner
  client (ADR 0020).
- Portal sign-in through WSO2: OIDC code flow with PKCE, 20-minute sliding and 8-hour absolute sessions, sign-out that
  ends the WSO2 session and revokes the cookie ticket, back-channel logout, just-in-time user linking that never
  follows an institution change in the token, role landing pages and a sign-in failure page with the trace id (ADR 0019).
- API authentication with WSO2 access tokens: strict issuer, audience, algorithm and token-type checks; the bank comes
  from `iam.ApiClients`, never from token claims; `GET /v1/me` and `GET /v1/institutions/{code}` scoped to the caller's
  bank, with the same 404 for other banks and unknown codes (ADR 0018).
- Hash-chained, HMAC-signed audit trail with sign-in, sign-out and de-duplicated access-denied events (ADR 0016).
- Authorization policies for every role, API scopes and institution membership; MFA requirement for approvals.
- `scripts/smoke-wso2.sh`: end-to-end identity smoke test (tokens, API scoping, password and TOTP logins for every MFA
  user, provisioner scopes, closed self-service including Basic authentication, optional Playwright browser sign-in).
- `scripts/init-env.sh` (random secrets; completes an existing `.env` without rotating it and rejects example values),
  `scripts/dev-certs.sh` (dev CA and keystores, staged and moved into place together), `scripts/dev-secrets.sh`
  (copies local secrets into user-secrets). No script passes a secret on the command line.
- Troubleshooting guide section for sign-in, tokens and WSO2.

- Phase 3: validation engine that runs every active rule of a template version (required, data type, range,
  cross-field and variance) in field order, with per-field findings and detail such as "Entered 1,400; calculated
  1,300." The same engine validates web entries, uploads and the demo seed.
- In-house decimal expression language for cross-field rules: field references, `+ - * /`, `Min`, `Max`, `Abs`, with
  length and depth limits (ADR 0021).
- Strict, culture-independent value parsing: invariant numbers with optional thousand groups, precision and column
  limits, ISO dates, yes/no booleans and percentages with an optional `%`.
- Variance rules compare with the last approved return for the previous period or the same period last year.
- Bank return pages: obligations overview, entry form by section with inline findings, draft save with optimistic
  concurrency and a clear conflict message, re-validation, warning justifications and upload history (ADR 0023).
- Excel and CSV template download and upload with ClosedXML: extension, signature, macro, zip-bomb, row and cell
  limits; percentage-formatted cells read as percentages; CSV formula-injection guard that round-trips; accepted
  files are kept as evidence in `returns.StoredFiles` and never served (ADR 0022).
- Template administration: draft versions copied from the published one, field and rule editing, publish, retire and
  delete draft (ADR 0009 amendment).
- Log events 50xx (templates), 51xx (returns) and 52xx (uploads); validation and upload metrics and a
  `returns.validate` span; troubleshooting section for returns, templates and uploads.

- Phase 4: the submission workflow in the portal. Checkers submit from the return page with a comment (late returns
  are flagged against the due date); supervisor reviewers pick returns up and send them back for correction;
  approvers who did not review a return approve or reject it, behind the TOTP approval policy. Every step goes through
  one `TransitionReturn` command, and pages offer only the steps `Submission.Permits` allows (ADR 0025).
- Supervision worklist (waiting for a reviewer, under review, sent back to banks, decided in the last 30 days) with
  bank, return type and late filters, and a review page with values, findings, justifications, uploads and history.
- Bank return pages show the workflow history and the supervisor's comment when a return comes back or is rejected.
- Log events 53xx (workflow steps and refusals), `regreturns.workflow.transitions` and
  `regreturns.workflow.late_submissions` metrics and a `returns.transition` span.
- Data-change audit: every save in the portal and the API appends one entry per changed aggregate to the hash chain,
  in the same transaction, with before and after values as a JSON change document, the acting user, IP address and
  trace id. E-mail addresses and file contents are left out; seeding writes no entries (ADR 0024).
- Auditor screen: the audit trail newest first with action, entity, actor and entity-history filters, each entry's
  changes, and chain verification that names the first missing, re-ordered or edited entry and records the check.
- Log events 3003 (chain intact, with the head) and 3004 (chain broken), `audit.append-data-changes` and
  `audit.verify-chain` spans; troubleshooting sections for workflow steps and the audit trail.
- Phase 5: REST API v1 for bank systems with URL-segment versions, an OpenAPI 3.1 document at `/openapi/v1.json`
  and Swagger UI at `/swagger` that signs in with client credentials as the read-only demo client (ADR 0027).
- `GET /v1/return-types` and `GET /v1/return-types/{code}/template?period=` (field codes and rules of the template
  in force for a period); `GET /v1/submissions` (paged with `Link` headers, filters by status, return type and
  period), `GET /v1/submissions/{id}` (values and workflow history) and `GET /v1/submissions/{id}/validation`.
- `POST /v1/submissions` delivers a whole return into a new or open draft and validates it; a bank checker submits it
  in the portal. Each API client acts through its own client user, so the audit trail and segregation of duties treat
  it as a bank maker (ADR 0026).
- Idempotency keys on every `POST`: stored replies for 24 hours, `Idempotent-Replayed` on replays, 422 for a key
  reused with another body, 409 with `Retry-After` while the first request runs, and takeover of abandoned claims.
- Per-client fixed-window rate limit (120 requests a minute by default) with a 429 problem and `Retry-After`.
- Problem details with a stable `code` and `traceId` on every API error; another bank's ids answer the same 404 as
  unknown ids.
- Log events 3301-3307 (idempotency and rate limits) and 5106-5107 (deliveries), the `regreturns.api.*` metrics, and
  a troubleshooting section for the API.
- Phase 6: reports dashboard for every role, one return type at a time: summary cards, a bank × period compliance
  grid (on time, late, overdue, not due yet) with a legend and dates on every cell, the overdue list across return
  types with days overdue and the reason, a Chart.js trend of validation findings by rule with a data table, and
  key-ratio sparklines with low, high and latest values. Bank staff see their own bank; regulator staff see every
  bank but no bank's draft (ADR 0028).
- SQL views in a new `reporting` schema (`ObligationCompliance`, `SubmittedFindings`, `ApprovedValues`) read with
  Dapper, with SqlClient retries on open.
- Compliance report export as Excel (grid, overdue and obligations sheets) and PDF (A4 landscape), each recorded in
  the audit trail as `ReportExported` with its scope and format.
- Key ratios configured in `Reports:KeyRatios`, labelled from the latest published template.
- Log event 5401 (report exported), the `regreturns.reports.exports` metric, and a troubleshooting section for
  reports.
- Phase 7: legacy data migration. `regreturns-migrator legacy --source <folder> [--dry-run] [--report <folder>]`
  migrates CSV exports of the legacy returns system (VRRS) as approved returns marked `Source=Migration`, filed by the
  `system.migration` account, which cannot sign in (ADR 0029).
- A JSON mapping names bank spellings, columns and cleansing rules: several date formats, Excel serial dates,
  thousand separators, currency codes, percent signs, accounting brackets and null tokens. Decimal commas and other
  ambiguous values are refused, never guessed; the last row for a bank and period wins.
- Every row is validated with the template rules in force for its period; a portal return is never replaced, and a
  period migrated before is skipped and reconciled again.
- One transaction per run: values are read back and reconciled with the source by return, field, bank and period; it
  commits only if it reconciles and is not a dry run. Exit codes 0 reconciled, 1 failed, 2 mismatch.
- Runs, their files with SHA-256 hashes and every row error are kept in a new `migration` schema; migrated returns
  and runs join the audit chain as the migrator.
- Console tables and five CSV reports (summary, row errors, reconciliation detail, by bank, by period), with the CSV
  formula guard.
- `legacy-samples` regenerates deterministic sample exports with planted defects, committed under `samples/legacy`
  with a README of every defect and the expected result.
- Log events 2101 to 2106, a data migration guide (`docs/DATA-MIGRATION.md`) and a troubleshooting section.

- Phase 8: advisory insights on the supervision review page (ADR 0030). A reviewer or approver presses
  *Generate insight*; Claude writes a headline, observations and questions through the official Anthropic SDK
  (`claude-opus-5-5`, effort medium, structured JSON output), and fixed rules write the same structure without an API
  key, after a timeout, rate limit, provider error or refusal, or for an unusable answer.
- The payload holds only numeric fields, their approved figures for the previous period and the same period last
  year, the changes, and the failed rules' template text; never the bank, people, justifications, comments or text
  field values. A guard allows only template text, codes, enumeration values and period labels, and no e-mail
  addresses, before anything is sent.
- Largest movements and failed rules are computed in code; the panel shows who wrote the insight, why fixed rules
  stood in, what was shared and its SHA-256, and labels every insight as advisory. Model text is stored as plain text
  and HTML-encoded.
- Every generation is an `InsightGenerated` audit event, recorded before the insight is stored, with both digests;
  insights are kept in `returns.ReturnInsights` and reused while nothing changes.
- `Ai` settings validated at start-up, an optional `ANTHROPIC_API_KEY` in `.env` copied to user-secrets, log events
  5501 to 5506 and 5511 to 5515, the metrics `regreturns.insights.generated`, `regreturns.insights.duration` and
  `regreturns.ai.tokens`, a guide (`docs/AI-ASSISTANT.md`) and a troubleshooting section.

### Changed

- SQL Server 2025 replaces 2022 for local Docker Compose and the integration tests (ADR 0013).
- The platform administrator role is `portal_admin`: WSO2 reserves the `system_` prefix.
- Cross-field rules use the in-house expression language instead of NCalc (ADR 0021).
- An obligation can have one live return; a filtered unique index replaces the plain obligation index (ADR 0023).
- Template `EffectiveFrom` is the first reporting period a version applies to; retiring is an explicit step (ADR 0009).
- Collections load with split queries by default.
- Seeded templates are in force from 1 January 2024, so migrated history has a template (ADR 0029).
- `AppUser.Create` refuses reserved user names (`system.migration`, the API client users' prefix).
