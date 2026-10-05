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

### Changed

- SQL Server 2025 replaces 2022 for local Docker Compose and the integration tests (ADR 0013).
- The platform administrator role is `portal_admin`: WSO2 reserves the `system_` prefix.
- Cross-field rules use the in-house expression language instead of NCalc (ADR 0021).
- An obligation can have one live return; a filtered unique index replaces the plain obligation index (ADR 0023).
- Template `EffectiveFrom` is the first reporting period a version applies to; retiring is an explicit step (ADR 0009).
- Collections load with split queries by default.
