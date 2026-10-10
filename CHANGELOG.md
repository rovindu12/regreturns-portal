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

- Phase 9: the public demo (ADR 0031, `docs/DEMO.md`). Demo mode (`Demo:Enabled`) is off by default and on in
  Development; only then does the portal show the demo pages, the banner and the reset button.
- Anonymous pages: a landing page with the architecture diagram (Mermaid source in `docs/diagrams`, rendered ahead of
  time to plain SVG by `scripts/render-diagrams.sh`), a feature tour and who does what; `/demo` with every demo
  account by role, *Sign in as* buttons that pass a WSO2 login hint, the shared password, the authenticator keys as
  text and QR code, and the read-only Swagger client; `/demo/guide`, the guided tour; `/status`, the portal, database
  and WSO2 as operational, degraded or down (cached 15 seconds), with the last and next reset.
- Demo reset: the workload (returns, templates, obligations, uploads, insights, migration runs, idempotency records)
  is deleted and seeded again for today in one transaction; institutions, people, API clients and the audit chain are
  kept, and a `DemoReset` audit event marks the boundary. Nightly at 03:00 UTC (Cronos) and from the administrator's
  *Reset demo* button, at most once per ten minutes, never twice at once (application lock), and never on a database
  with people who are not demo accounts. Log events 5601 to 5605, the metrics `regreturns.demo.resets` and
  `regreturns.demo.reset.duration`.
- Sandboxed administration: `/admin/users` lists people and API clients and can disable or re-enable a person's portal
  access; demo, system and client accounts and the administrator's own account are refused (log events 3011, 3012).
- `scripts/demo-scenario.sh` plays the guided tour in headless Chromium against a running demo, WSO2 sign-in with TOTP
  included, reading the credentials from `/demo` as a visitor would; the browser smoke tests share a launcher that
  pins exactly the certificates they verified.
- The seed builds for any reset date: a test seeds on the first of every month for ten years.

- Phase 10: security hardening (ADR 0033, `docs/SECURITY.md` with the threat model and an OWASP ASVS 5.0 level 2
  self-assessment). Security headers on every answer of both hosts: a Content Security Policy with a fresh script
  nonce per response and no inline code (a tag helper adds the nonce, a unit test scans the views), `nosniff`,
  `frame-ancestors 'none'`, `Referrer-Policy`, `Permissions-Policy`, COOP and CORP, HSTS for a year, no `Server`
  header, and `no-store` for signed-in answers. The API allows nothing; Swagger UI has its own policy.
- Tests that every portal form refuses a post without its anti-forgery token, and that the anonymous endpoints are
  exactly the public pages, sign-in pages, back-channel logout and health checks.
- SQL Server forces TLS with a certificate from the development CA (`scripts/dev-certs.sh`, a one-off
  `sqlserver-tls` compose job); the apps, the migrator, WSO2's JDBC driver, `sqlcmd` and the Testcontainers fixture
  all use TDS 8 strict encryption with the certificate pinned.
- Administrators can reset a person's authenticator from `/admin/users`: the portal opens a time-limited enrolment
  window in WSO2 over SCIM (provisioner client, back channel), the sign-in script lets the person enrol a new
  authenticator at their next sign-in, and every window is an audit event (`TotpEnrolmentOpened`; log events 3013 to
  3015; ADR 0032).
- WSO2 locks an account for five minutes after five failed sign-ins (IamBootstrap `account-lock` step).
- `/admin/diagnostics` for administrators: health checks with timings and errors, build and runtime, database
  version, connection encryption and migrations, the audit chain head and the effective settings, never secrets.
- Edge proxy configuration `deploy/caddy/Caddyfile` (used from phase 11): only WSO2's sign-in paths are public, the
  console and management APIs and Seq need an allowlisted address, tokens are redacted from access logs.
  `scripts/check-caddy.sh` runs it against stub upstreams with 49 checks, in CI.
- CodeQL analysis of C# and the workflows on every push, pull request and weekly.
- `scripts/demo-scenario.sh` fails on any Content Security Policy violation reported by the browser.

- Phase 11: containers, a release pipeline and one-server hosting (ADR 0034, `docs/DEPLOYMENT.md`,
  `docs/DR-RUNBOOK.md`). One multi-stage `Dockerfile` builds the portal, the API, the migrator and IamBootstrap on
  chiselled, non-root ASP.NET Core images, stamped with the commit; WSO2 and its database job are images too.
- `--health-probe`: the apps check their own readiness for Docker's `HEALTHCHECK`, since the images have no shell.
- `docker-compose.prod.yml`: Caddy is the only service with published ports; an internal network for SQL Server; no
  capabilities, no privilege gain, read-only root file systems and memory caps; Seq on the server; the portal's
  data-protection keys on a volume; WSO2 behind the proxy with its public URL.
- Forwarded headers trusted only from the edge network (`ReverseProxy:KnownNetworks`), so the audit trail and rate
  limits see the client's address and the apps see HTTPS.
- A least-privilege database login for the portal and the API (`regreturns_runtime` role: no schema changes, and
  `DENY UPDATE, DELETE` on the audit table), created by the `app-db-init` job.
- Migrator `verify-audit`: walks the audit hash chain and exits 0 intact, 1 failed or 2 broken (log events 2003-2005).
- The public status page also shows the REST API (`Status:ApiHealthUrl`), without making it part of the portal's
  readiness.
- `deploy/regreturns.sh`: `init`, `deploy <sha>`, `ci-deploy`, `backup`, `restore <stamp>`, `verify-audit`,
  `demo-users`, `smoke`, `status`, `sql`, `compose`. Backups use page checksums and `RESTORE VERIFYONLY`, keep 14 days
  with a `SHA256SUMS` manifest and can go off the server encrypted with `age` through rclone. A restore checks the
  checksums, re-maps logins, applies newer migrations and starts nothing unless the audit chain verifies.
- `deploy/server-setup.sh`: Docker from its repository with the key fingerprint checked, unattended upgrades, swap,
  ufw, key-only SSH, the `regreturns` user and systemd timers for the nightly backup and demo-user reset.
- Release workflow: builds the six images, writes CycloneDX SBOMs, fails on fixable high or critical vulnerabilities
  (Trivy), deploys the production stack on the runner, runs the browser identity smoke test, a backup, change,
  restore and smoke again, pushes the images to GHCR on `main` and deploys over an SSH key restricted to one command
  once a server is configured.
- CI also scans the whole history for secrets (gitleaks) and checks every shell script (ShellCheck).
- Dependabot proposes updates for the Dockerfiles' and compose files' images; `ImagePinTests` keeps the copies of
  each pin in step.
- Phase 12: architecture guide with C4 context, container and component diagrams, the data model and the life of a
  return; identity and access, API and user guides; a backlog of user stories by sprint with what is left; a README
  with screenshots, the role mapping and the demo logins (ADR 0035).
- The guided tour (`scripts/demo-scenario.sh`) checks every portal page against WCAG 2.2 A and AA with axe (and the
  public pages at phone width too), raises non-performing loans so the checker justifies real warnings, looks round
  the administration pages, and with `--screenshots` captures the documentation's pictures with the published
  secrets masked. The Release workflow runs it against the production stack and keeps the screenshots.
- A "Page not found" page (and pages for other empty error answers) in the portal's layout, with a reference to quote.
- CI job *Documentation*: every relative link, image and anchor in the Markdown resolves and every ADR is indexed
  (`scripts/check-docs.py`).

### Changed

- SQL Server 2025 replaces 2022 for local Docker Compose and the integration tests (ADR 0013).
- The platform administrator role is `portal_admin`: WSO2 reserves the `system_` prefix.
- Cross-field rules use the in-house expression language instead of NCalc (ADR 0021).
- An obligation can have one live return; a filtered unique index replaces the plain obligation index (ADR 0023).
- Template `EffectiveFrom` is the first reporting period a version applies to; retiring is an explicit step (ADR 0009).
- Collections load with split queries by default.
- Seeded templates are in force from 1 January 2024, so migrated history has a template (ADR 0029).
- `AppUser.Create` refuses reserved user names (`system.migration`, the API client users' prefix).
- Local connection strings use `Encrypt=Strict` with SQL Server's certificate pinned instead of
  `TrustServerCertificate=True` (ADR 0033 supersedes that part of ADR 0014); `sqlcmd` needs `-Ns -J <certificate>`.
- Log redaction also masks OAuth parameters (`code`, `id_token_hint`, `logout_token`, `client_assertion`), keys and
  credentials; request logs state explicitly that they carry no query string.
- SQL Server is pinned to a cumulative update (`2025-CU9-ubuntu-24.04`) everywhere instead of `2025-latest`.
- `Wso2:TrustedCaPath` must name a readable PEM file with a certificate; the hosts refuse to start otherwise.
- `scripts/dev-certs.sh` leaves the certificate folders and the CA certificate readable for the containers whatever
  the umask; `scripts/smoke-wso2.sh` can trust the system store (`WSO2_CA=system`) and read the generated settings
  from another file (`GENERATED_FILE`); `scripts/init-env.sh` also creates the app database and Seq passwords.
- Accessibility: links inside alerts use Bootstrap's `alert-link`, every table has a caption, tables that scroll
  sideways can be scrolled from the keyboard, and table wrappers no longer scroll a pixel vertically.
- Least privilege: the provisioner client is authorised only for the three SCIM user scopes the portal requests
  (list, view, update); IamBootstrap withdraws the create, delete and role scopes from a client set up earlier
  (ADR 0032).

### Fixed

- The API refused to start in Development: it registered the report handlers without a reporting read model. Hosts
  without reports now get refusing fallbacks, and both hosts are built with Development's service validation in tests.
