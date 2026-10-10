# CLAUDE.md

Guidance for AI assistants and contributors working in this repository. Keep it current: update it in the same
change that alters a convention, command or architectural rule.

## What this is

RegReturns is a portfolio-grade Regulatory Returns Portal. Fictional licensed banks submit periodic returns
(Monthly Liquidity Return `MLR`, Monthly Deposits and Advances Return `MDA`, Quarterly Capital Adequacy Return `QCAR`)
to a fictional regulator, the **Bank of Valoria** (currency VLD). Returns are validated, go through a maker-checker
workflow and appear on supervisory dashboards. WSO2 Identity Server is the identity provider (from phase 2).

Never use a real central bank's or real bank's name, logo or branding. All people and institutions are fictional.

The full plan is in [docs/IMPLEMENTATION-PLAN.md](docs/IMPLEMENTATION-PLAN.md); decisions are in [docs/adr](docs/adr).

## Build phases

Work proceeds in 12 phases (plan §10). After each phase: zero-warning build, all tests green, a Conventional Commit
pushed, CI green and a short summary, then **continue straight into the next phase** (the owner asked for full
automation on 2026-10-04). Stop only for decisions that are genuinely the owner's or inputs only they can give, such
as the domain and server right before the phase 11 deploy.

| Phase | Status |
|---|---|
| 1. Scaffold, domain, DB, seed (+ observability baseline) | done |
| 2. WSO2 in Docker, IamBootstrap, OIDC login, role policies | done |
| 3. Templates, submission, validation engine | done |
| 4. Workflow, queues, audit interceptor, auditor verify screen | done |
| 5. Web API: v1 endpoints, idempotency, paging, rate limits, Swagger | done |
| 6. Dashboards and reports: compliance grid, overdue, findings trend, key ratios, Excel/PDF export | done |
| 7. Migration tool: legacy CSV generator, dry-run migrator, mapping, error report, reconciliation | done |
| 8. AI assistant: insight service, Anthropic provider, rule-based fallback, reviewer panel, audited calls | done |
| 9. Demo portal: landing page, try-the-demo, guided scenario, status page, demo reset | done |
| 10. Security hardening: headers and CSP, anti-forgery audit, SQL TLS, account lock, authenticator reset, diagnostics, edge, CodeQL | done |
| 11. Containers, release pipeline, one-server hosting, backups and restore (live deploy needs the domain and server) | done |
| 12. Docs and polish: C4 architecture, IAM, API and user guides, backlog, screenshots, axe in the tour, docs link check | done |

## Commands

```bash
# Prerequisites: .NET 10 SDK, Docker, openssl, jq. Run everything from the repository root.
scripts/init-env.sh                        # creates .env with random secrets (mode 600, git-ignored); on an existing
                                           # .env only adds missing keys and fails on example values, never rotates
scripts/dev-certs.sh                       # dev CA, WSO2 keystores and SQL Server's certificate in .certs/ (ADR 0015, 0033)
docker compose up -d                       # SQL Server, WSO2 (https://localhost:9443/console), Seq (http://localhost:8081)
scripts/dev-secrets.sh                     # copies .env (and .env.generated) into user-secrets for every project
                                           # (an optional ANTHROPIC_API_KEY becomes the portal's Ai:Anthropic:ApiKey)

dotnet run --project tools/RegReturns.Migrator -- migrate-db --seed   # schema + demo data (idempotent)
dotnet run --project tools/RegReturns.Migrator -- legacy --source samples/legacy --dry-run --report out/legacy
                                                                      # legacy CSV migration (ADR 0029); drop --dry-run
                                                                      # to commit; exit 0 reconciled, 1 failed, 2 mismatch
dotnet run --project tools/RegReturns.Migrator -- legacy-samples --out samples/legacy  # regenerate the sample exports
dotnet run --project tools/RegReturns.IamBootstrap -- apply           # WSO2 claims, API, apps, roles, bank clients,
                                                                      # demo users; writes .env.generated (idempotent)
scripts/dev-secrets.sh                                                # again, to pick up the portal client secret
dotnet run --project tools/RegReturns.IamBootstrap -- demo-users      # reset demo users only (passwords, roles, TOTP)
dotnet run --project src/RegReturns.Web                               # https://localhost:7101
dotnet run --project src/RegReturns.Api                               # https://localhost:7201 (/swagger, /openapi/v1.json)
scripts/smoke-wso2.sh --browser                                       # identity smoke test (needs Web + Api running;
                                                                      # --browser needs Node with Playwright)
scripts/check-caddy.sh                                                # edge config against stub upstreams (Docker; CI)
scripts/demo-scenario.sh [--reset] [--screenshots]                    # plays the guided tour in Chromium against a
                                                                      # running demo (changes data; --reset first), with
                                                                      # axe (WCAG 2.2 AA) and CSP checks on every page;
                                                                      # --screenshots refreshes docs/images
python3 scripts/check-docs.py                                         # Markdown links, images, anchors, ADR index (CI)
scripts/render-diagrams.sh                                            # docs/diagrams/*.mmd -> wwwroot/img/*.svg (needs
                                                                      # Node; CHROMIUM=/path to skip the download)
dotnet run --project tools/RegReturns.Migrator -- verify-audit        # walk the audit hash chain: 0 intact, 1 failed,
                                                                      # 2 broken

# Containers and the server (docs/DEPLOYMENT.md, ADR 0034):
docker buildx build --target web -t regreturns-web:dev .     # targets: web, api, migrator, iam-bootstrap
deploy/regreturns.sh <command>                               # on the server: init, deploy <sha> [--no-pull], backup,
                                                             # restore <stamp> [--yes], verify-audit, demo-users, smoke,
                                                             # status, sql, compose; ci-deploy is for the CI key only
deploy/server-setup.sh --admin-key ... --ci-key ...          # once, as root, on a fresh Ubuntu 24.04 server

dotnet build RegReturns.slnx                                   # warnings are errors
dotnet test --project tests/RegReturns.UnitTests               # fast, no Docker
dotnet test --project tests/RegReturns.IntegrationTests        # needs Docker (Testcontainers SQL Server)
dotnet test --project tests/RegReturns.UnitTests --coverage --coverage-output-format cobertura \
  --coverage-output unit.cobertura.xml --coverage-settings coverage.config --results-directory "$PWD/TestResults"
python3 scripts/check-coverage.py TestResults/unit.cobertura.xml RegReturns.Domain=80   # CI coverage gate
dotnet format RegReturns.slnx --verify-no-changes              # CI fails on formatting drift

# New EF migration (Infrastructure is both project and startup; a design-time factory supplies the context):
dotnet ef migrations add <Name> -p src/RegReturns.Infrastructure -s src/RegReturns.Infrastructure -o Persistence/Migrations
```

## Architecture

```
src/RegReturns.Domain          Entities, value objects, workflow rules. No package dependencies.
src/RegReturns.Application     Use-case handlers, IAppDbContext, telemetry names. References EF Core abstractions only.
src/RegReturns.Infrastructure  EF Core context, configurations, migrations, seeding, DI; Dapper reporting read model and
                               the compliance report renderer (ClosedXML, QuestPDF); the legacy migration (`Legacy`); the
                               Anthropic insight narrator (`Ai`, the only code that references the Anthropic SDK).
src/RegReturns.ServiceDefaults Serilog, OpenTelemetry, health endpoints, ProblemDetails, exception handler.
src/RegReturns.Web             MVC portal (Razor + Bootstrap 5).
src/RegReturns.Api             REST API v1 for bank systems: reference data, reads, draft delivery (ADR 0026, 0027).
tools/RegReturns.Migrator      `migrate-db [--seed]`, `seed`, `legacy` (CSV migration, ADR 0029), `legacy-samples`,
                               `verify-audit` (hash chain check; a restore starts nothing unless it passes).
tools/RegReturns.IamBootstrap  `apply`, `demo-users`: idempotent WSO2 setup over its REST APIs (ADR 0017, 0020).
scripts/smoke-wso2.sh          End-to-end identity smoke test against a running WSO2 + API (`--browser`: + portal).
deploy/caddy/Caddyfile         Edge proxy (phase 11): TLS, HSTS, WSO2 sign-in paths public, admin paths allowlisted.
deploy/sqlserver/mssql.conf    Forces TLS with the certificate the `sqlserver-tls` compose job installs (ADR 0033).
scripts/demo-scenario.sh       Browser run of the guided demo tour; `scripts/smoke/*.cjs` hold the Playwright parts
                               (`demo-session.cjs` signs in from the demo page, `accessibility.cjs` runs axe). The
                               Release workflow runs it against the production stack (ADR 0035).
scripts/check-docs.py          Documentation link check (CI job "Documentation").
docs/                          ARCHITECTURE (C4), IAM, API, USER-GUIDE, BACKLOG and the guides; images/ are captured
                               by `demo-scenario.sh --screenshots`, never edited by hand.
scripts/render-diagrams.sh     Renders the Mermaid sources in docs/diagrams to the committed SVGs.
samples/legacy                 Generated legacy (VRRS) exports with planted defects and their mapping (see its README).
Dockerfile                     One multi-stage build with the targets web, api, migrator and iam-bootstrap (ADR 0034).
docker-compose.prod.yml        The production stack behind Caddy; docker-compose.yml is the development stack.
deploy/                        Caddyfile, WSO2 and its db-init images, SQL Server init jobs, `regreturns.sh` (every server
                               operation) and `server-setup.sh` (a fresh host).
.github/workflows/release.yml  Builds the six images, SBOMs, Trivy gate, the production stack end to end on the runner
                               (browser smoke test, backup and restore), pushes to GHCR on main, deploys when configured.
tests/RegReturns.UnitTests     Domain, seeding, redaction and architecture tests.
tests/RegReturns.IntegrationTests  Testcontainers SQL Server, WebApplicationFactory host tests.
```

Dependency rule: Domain ← Application ← Infrastructure ← hosts. Enforced by `tests/RegReturns.UnitTests/Architecture`.

Key domain rules (see `src/RegReturns.Domain/Submissions/Submission.cs`):
- Workflow: Draft → Submitted → UnderReview → ReturnedForCorrection / Approved / Rejected (`SubmissionWorkflow`).
- Maker creates and edits; checker submits and must not be the preparer or last editor; reviewer picks up;
  approver approves or rejects and must not be the reviewer. Supervisory steps require regulator staff (no institution).
- Submit requires values, a validation run after the last edit, no errors and a justification (≥20 chars) per warning.
- One live (not rejected) submission per obligation (`UX_Submissions_LiveObligation`). Saving unchanged values is not an
  edit. Forms post the `EditVersion` they were rendered with; an older one is `Submission.EditConflict` (ADR 0023).
- `Submission.Permits(action, actor)` is the one check of state, role, organisation and segregation of duties;
  `ActionsFor(actor)` lists the steps a page may offer. Every step runs through the `TransitionReturn` command (ADR 0025).
- Bank staff see their bank's returns; regulator staff see a return only once it has been submitted
  (`ReturnVisibility.VisibleTo`). Anything else is `Submission.NotFound` (404).

Templates and validation (see `src/RegReturns.Domain/Templates` and `Validation/ValidationEngine.cs`):
- A submission is captured with the published version whose `EffectiveFrom` (first period start) is the latest on or
  before the period start; one draft version per return type; retire is explicit (ADR 0009).
- `ValidationEngine` is pure: Required and DataType report blanks and bad values once; Range, CrossField and Variance
  only judge parsed numbers; findings come in field order, then rule type, then code. Values are parsed only by
  `FieldValueParser` (invariant culture, precision and column limits) in every channel.
- Cross-field expressions use `RuleExpression` (ADR 0021): `[CODE]`, numbers, `+ - * /`, `Min`, `Max`, `Abs`.
- Uploads (`.xlsx`, `.csv`) are checked by content, stored in `returns.StoredFiles` and never served (ADR 0022).
- Comments are required on every step except "start review". Returning for correction bumps `Revision`.
- `IsLate` is set on first submission if after the obligation's due date (UTC).

Audit trail (see `src/RegReturns.Infrastructure/Auditing`, ADR 0016 and 0024):
- Hash chain: gap-free `Sequence`, HMAC-SHA256 over `AuditEntry.ToCanonicalString()` plus the previous hash. Every
  writer (`AuditTrail.RecordAsync` for events, audited saves for data changes) takes the `sp_getapplock` chain lock
  (`AuditChain`) inside its transaction. An `INSTEAD OF UPDATE, DELETE` trigger makes the table append-only (51001).
- In the hosts every `SaveChanges` audits itself: one entry per changed aggregate root, in the same transaction, with a
  JSON change document (`AuditChanges`) and the actor from the host's `IAuditContext`. Contexts built without an
  `IDataChangeAuditor` (migrator, seeding, design time, `SqlServerFixture.CreateContext`) write no entries.
- Never add `AuditEntry` rows directly. Mark a property that must not be recorded `[NotAudited]` (with a unit test).
  A new canonical field may only be appended, and only when not null, so old entries keep their hashes.

API (see `src/RegReturns.Api`, ADR 0026 and 0027):
- The API is a maker channel: `POST /v1/submissions` delivers a whole return (fields left out become blank) into a
  new or open draft (`Source=Api`) and validates it; findings are part of the answer, not a refusal. A bank checker
  justifies warnings and submits in the portal. Each `ApiClient` acts through its client user (`AppUser.ForApiClient`:
  bank maker, no e-mail, no WSO2 link, cannot sign in); in the API `ICurrentActor` is `ClientActor`.
- URL-segment versions (`v{version:apiVersion}`), `[ApiVersion(1)]` on every controller. Every action names a policy;
  every `POST` is `[Idempotent]` (unit tests check both). Success types declare `application/json` in
  `ProducesResponseType`; never put `[Produces]` on a controller (see Gotchas).
- Refusals go through `this.Problem(error)` (status from `ApiProblems.StatusFor`: 400 malformed, 403 caller, 404
  `*.NotFound`, 409 state, 422 rule) and `this.InvalidRequest()` for model errors (`Request.Invalid`); both are
  `application/problem+json` with `code` and `traceId`. Lists page with `page`/`pageSize` (≤100) and a `Link` header.
- Idempotency (`Api:Idempotency`): keys per client, fingerprint of method, path and body; replays carry
  `Idempotent-Replayed: true`; 403, 409, 429 and 5xx answers release the key instead of being stored.
  Rate limit (`Api:RateLimit`): fixed window per client id (per IP without a token), controllers only.

Reports (see `src/RegReturns.Application/Reporting` and `src/RegReturns.Infrastructure/Reporting`, ADR 0028):
- Aggregates come from SQL views in the `reporting` schema (`ObligationCompliance`, `SubmittedFindings`,
  `ApprovedValues`), created in the `AddReportingViews` migration and read with Dapper by `ReportingReadModel`. They
  are not in the EF model: a migration that renames a column they use must recreate the view.
- `Compliance.StateOf` is the one rule for a cell (open and past due is overdue, open otherwise not due yet, else late
  or on time by first submission); the window is the last `Reports:MonthsShown`/`QuartersShown` completed periods.
  `ReportBuilder` scopes every query: bank staff see their bank, regulator staff every active bank but never a
  draft's status. Shaping is pure in `ReportShaping`; labels for pages and files are in `ReportLabels`.
- Key ratios are configuration (`Reports:KeyRatios`, return type and field); labels come from the latest published
  template. Charts: vendored Chart.js in `wwwroot/lib/chart.js`, drawn by `wwwroot/js/reports.js` from `data-chart`
  JSON, with the same figures as text on the page.
- Every export goes through `ExportComplianceReport`, which records `AuditAction.ReportExported` (scope and format,
  never figures), logs 5401 and counts `regreturns.reports.exports`.

Legacy migration (see `src/RegReturns.Infrastructure/Legacy`, ADR 0029 and docs/DATA-MIGRATION.md):
- A JSON mapping (`LegacyMapping`) names the cleansing rules, each bank's legacy spellings and each file's columns;
  cleansing is pure (`LegacyCleansing`) and refuses to guess (no decimal commas). The last row per return type, bank and
  period wins; earlier rows are `Legacy.Superseded`. Row error codes are `Legacy.*`, run errors `Migration.*`.
- Rows load through `Submission.Migrate` (approved, `Source=Migration`, the `system.migration` account, which cannot
  sign in) after the same `ValidationEngine`; error findings reject the row, warnings keep a fixed justification. A
  period the portal holds a return for is never replaced; one already migrated is skipped and reconciled again.
- One transaction: load, read back, `Reconciliation.Compare`; commit only if not a dry run and reconciled. Every run
  (with row errors) is kept in the `migration` schema, and the migrator's audited saves put returns and runs in the
  hash chain (actor `regreturns-migrator`).

Advisory insights (see `src/RegReturns.Application/Insights`, `src/RegReturns.Infrastructure/Ai`, ADR 0030 and
docs/AI-ASSISTANT.md):
- Supervision reviewers and approvers press *Generate insight* (`POST .../insight`); the review page renders the
  panel through the `ReturnInsight` view component and never calls a provider itself.
- `InsightPayloadBuilder` (pure) is the only source of the payload: numeric fields (amount, whole number, percentage)
  with current and approved prior figures (`PriorFigures`, shared with `ReturnValidator`), changes, and findings with
  template text. Never add bank-authored text, names, comments or ids. `InsightPayloadGuard` allows only template text,
  codes, enumeration names and period labels; fix the builder when it fails, never widen the guard.
- Movers and breaches (`InsightFacts`) are computed in code; an `IInsightNarrator` writes only the headline,
  observations and questions. `AnthropicInsightNarrator` maps every failure to an `InsightErrors` error and the handler
  falls back to `RuleBasedNarrator`, recording the `InsightFallbackReason`. Model text is plain text; Razor encodes it.
- The `InsightGenerated` audit event (with both SHA-256 digests) is recorded before the `ReturnInsight` row is saved,
  and also when the caller cancels after the payload was sent. `ReturnInsight` is `[NotAudited]` and keeps the event's
  sequence. Insights are reused per revision and payload digest, except fallbacks other than "no API key".

Public demo (see `src/RegReturns.Application/Demo`, `src/RegReturns.Infrastructure/Demo`, ADR 0031 and docs/DEMO.md):
- Demo mode (`Demo:Enabled`) is off by default (on in Development). Only then do `/demo` and `/demo/guide` exist (404
  otherwise), the banner show and the reset run. `/`, `/demo`, `/demo/guide` and `/status` are anonymous and GET only;
  `PortalEndpointMetadataTests` lists them. Published credentials come only from `Demo:*` settings, never WSO2 or the DB.
- The reset (`ResetDemo` → `IDemoReset`, `DemoResetService`) deletes `WorkloadTables` in foreign-key order and seeds
  again with `DemoDataBuilder` for the kept institutions and users, in one transaction: applock `RegReturns.DemoReset`
  (no wait), the cooldown from the latest `DemoReset` audit entry (button only), the `Demo.NotADemoDatabase` interlock,
  then one `DemoReset` audit event under the chain lock. Institutions, users, API clients and audit entries are
  `KeptTables`. A new table must go in one of the two lists (`DemoResetTablesTests`).
- `DemoResetJob` runs `Demo:ResetSchedule` (Cronos, UTC) as the system actor; only the portal registers it (`AddDemo`),
  other hosts get `UnavailableDemoReset`. The WSO2 side is IamBootstrap `demo-users`, run on the host.
- Sandboxed administration (`UserAdministration`): disable or re-enable a person's access; demo, system and client
  accounts and one's own account are refused. Log events 56xx (reset) and 3011-3012 (access changes).

Hosting (see `deploy/`, ADR 0034, docs/DEPLOYMENT.md and docs/DR-RUNBOOK.md):
- Every app and tool image runs on `mcr.microsoft.com/dotnet/aspnet:<patch>-noble-chiseled-extra` (the tools use
  ServiceDefaults, which needs ASP.NET Core) as uid 1654 with no shell: `--health-probe [path]` (`HealthProbe`, answered
  before the host is built) is the Docker `HEALTHCHECK`. Tool entry points are the assembly names
  `regreturns-migrator.dll` and `regreturns-iam-bootstrap.dll`.
- Behind Caddy: `ReverseProxy:KnownNetworks` (the edge subnet) trusts forwarded headers and moves the HTTPS redirect to
  the edge; `DataProtection:KeysPath` keeps the portal's keys on a volume; `Status:ApiHealthUrl` adds the API to the
  status page under the `status` tag, which is not part of the portal's readiness.
- The apps log in as `regreturns_app`, member of `DatabaseRoles.Runtime` only (database-wide data and execute rights,
  `DENY UPDATE, DELETE` on `audit.AuditEntries`); a new append-only table needs its own `DENY` (`RuntimeRoleTests`).
  The migrator, IamBootstrap and the backups use `sa`.
- `regreturns.sh` passes `.env`, `generated/.env.generated` (IamBootstrap) and `.env.release` (written by `deploy`; the
  deployed tag) to Compose in that order, and never a secret on a command line. Backups run on host systemd timers.
- Third-party images are pinned by tag (CI tools by digest) and `ImagePinTests` keeps each pin's copies equal: SQL
  Server in both compose files, the WSO2 db-init Dockerfile and `SqlServerFixture.Image`; WSO2 in its two Dockerfiles
  and `dev-certs.sh`; Caddy in `docker-compose.prod.yml` and `check-caddy.sh`. Change every copy in one PR.

Security (see ADR 0032, 0033 and docs/SECURITY.md):
- Both hosts send their headers through `UseSecurityHeaders` (ServiceDefaults) on every answer. The portal's CSP has a
  per-response nonce (`HttpContext.CspNonce()`), which `CspNonceTagHelper` puts on every `<script>`; views have no
  inline scripts, `<style>`, `style=` attributes, `on*=` handlers or `javascript:` URLs (`PortalContentSecurityPolicyTests`
  scans them). Scripts set styles through the CSS object model (`element.style`), which the policy allows. The API's
  policy is `default-src 'none'`; only `/swagger` has its own.
- Every portal POST needs the anti-forgery token (global filter); `PortalAntiforgeryTests` posts to all of them, and
  only back-channel logout may opt out. Never add `[ValidateAntiForgeryToken]` to an action: it adds nothing, and
  CodeQL, which does not see ASP.NET Core's global filters, then reports every other form (a test refuses it). A new `[AllowAnonymous]` endpoint must be added to the list in
  `PortalEndpointMetadataTests`.
- SQL Server forces TLS: connection strings use `Encrypt=Strict;ServerCertificate=<.certs/sqlserver/mssql.crt>`
  (never `TrustServerCertificate`), JDBC `encrypt=strict;serverCertificate=...`, and `sqlcmd -Ns -J <cert>`.
- Authenticator reset (`OpenTotpEnrolment`): the portal sets WSO2's `totp_enrolment_until` claim over SCIM with the
  provisioner client (`IIdentityDirectory`, `Wso2IdentityDirectory`), through the back channel; the adaptive script
  enrols the user at their next sign-in and closes the window. Audited as `TotpEnrolmentOpened`. IamBootstrap
  authorises the provisioner for exactly `Wso2IdentityDirectory.Scopes`: a new SCIM call adds its scope there.
- `/admin/diagnostics` shows settings as "set"/"not set" for secrets and origins for endpoints; a new setting shown
  there must never print a secret (`AdminDiagnosticsTests` checks the configured ones).

Documentation and accessibility (see ADR 0035):
- Docs are Markdown with Mermaid diagrams, changed in the same PR as the code; `scripts/check-docs.py` fails CI on a
  broken relative link, image or anchor, or an ADR missing from `docs/adr/README.md`.
- WCAG 2.2 AA: the guided tour runs axe on every portal page it visits and fails on any A or AA violation (WSO2's own
  pages are reported only). A new page needs a step in `scripts/smoke/demo-scenario.cjs`. Every table has a
  `<caption>` (visually hidden is fine); links inside an alert use `alert-link`; `.table-responsive` wrappers that
  overflow become focusable regions through `site.js`.
- An empty 4xx/5xx answer to a `GET` or `HEAD` is re-executed to `Home/HttpError` (status code pages, in the portal's
  layout); `POST`s are never re-executed, so the anti-forgery check never runs twice.

## Conventions

- .NET 10, C# latest, nullable on, `TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`, SonarAnalyzer and
  VS Threading analyzers. Fix warnings; suppress only with a `Justification` in a `SuppressMessage` attribute.
- Central Package Management (`Directory.Packages.props`) with lock files; CI restores in locked mode.
  After changing packages run `dotnet restore RegReturns.slnx` and commit the updated `packages.lock.json` files.
- XML doc comments on all public members in `src/` and `tools/` (CS1591 is an error).
- Async all the way with `CancellationToken`; methods returning tasks end in `Async` (MVC strips it from action names).
- Expected failures return `Result`/`Error` with stable codes (`Submission.CheckerIsMaker`); `DomainException` only for
  programming errors. Error catalogues live next to the aggregate (`SubmissionErrors`, `TemplateErrors`, `IdentityErrors`).
- Entities: private setters, factory methods, `Guid.CreateVersion7()` ids assigned in `Entity`.
- No magic strings: field codes, rule codes and schema names are constants (`MlrTemplate.TotalHqla`, `Schemas.Returns`).
- Logging: `[LoggerMessage]` source-generated methods with event ids grouped per area (1xxx persistence, 20xx migrator,
  21xx legacy migration, 30xx audit and identity, 31xx portal sign-in, 32xx API authentication, 33xx API idempotency
  and rate limits, 4xxx IamBootstrap, 50xx templates, 51xx returns and API deliveries, 52xx uploads and files, 53xx
  workflow steps, 54xx reports, 550x insights, 551x AI provider, 56xx demo reset, 9xxx hosting). Never log secrets, tokens, e-mails or return figures; the redaction enricher is a safety net, not a licence.
- Time: inject `TimeProvider`; store UTC `DateTimeOffset`; dates as `DateOnly`; parse numbers with `CultureInfo.InvariantCulture`.
- Config: options classes with `ValidateDataAnnotations().ValidateOnStart()`. Secrets only in user-secrets or environment variables.
  On the server `.env` (created by `deploy/regreturns.sh init`) also holds the host names, `APP_DB_PASSWORD`,
  `SEQ_ADMIN_PASSWORD`, `WSO2_ADMIN_ALLOWLIST` and the backup settings (`.env.example` lists them).
  Secret keys: `ConnectionStrings:RegReturns`, `Audit:HmacKey` (Web, Api and the migrator's `legacy` verb share it),
  `Oidc:ClientSecret` (Web), `Iam:Provisioner:ClientId` and `Iam:Provisioner:ClientSecret` (Web, for authenticator
  resets), `Ai:Anthropic:ApiKey` (Web, optional: without it fixed rules write insights).
  WSO2 settings: `Wso2:Authority` (public issuer base), `Wso2:TrustedCaPath`, optional `Wso2:BackchannelAuthority`
  (e.g. `https://wso2:9443/` inside Docker), `Iam:EnforceMfa`.
  IamBootstrap reads `.env` itself (between appsettings/user-secrets and real environment variables) and writes
  generated client secrets and the TOTP secret of every MFA demo user (`TOTP_SECRET_<USER>`) to `.env.generated`
  (mode 600, git-ignored).
- Scripts never put a secret on a command line (other local users can read the process list): curl gets credentials
  through `-K <(...)` or `-H @<(...)`, jq through `$ENV`, keytool through `:env`, sqlcmd through `SQLCMDPASSWORD`
  (with `-Ns -J <certificate>`: SQL Server refuses unencrypted connections).
- Web: controllers take use-case handlers per action with `[FromServices]` and never touch `IAppDbContext`
  (architecture test). Use cases resolve the caller with `ICurrentActor` (from the user record, never from claims)
  and scope bank data to the caller's institution; another bank's ids answer 404. Post-redirect-get with
  `TempData.Success/Error` (`_Flash` partial); re-render with the posted input when a save fails. No inline scripts
  (page scripts in `wwwroot/js`, loaded in the `Scripts` section).
- Identity: WSO2 is the only identity store; `AppUser` rows link to WSO2 by subject id. Role, claim and scope names are
  constants in `RoleNames`, `ClaimNames`, `ApiScopes` and `IamNames`. Change WSO2 only through IamBootstrap steps, never
  by hand in the Console, so a fresh environment can be rebuilt.
- Tests: `Method_or_behaviour_in_plain_words` names, Shouldly assertions, one behaviour per test. Host tests join the
  `HostedAppsDefinition` collection, sign in with `UseTestAuth()` (`X-Test-Claims` header) and use an `https://localhost`
  base address when they render signed-in pages (the session and antiforgery cookies are `Secure`). Integration tests that write data use `SqlServerFixture.NewDatabaseConnectionString()`.
  API tests register a fresh client per test (`ApiDatabase.RegisterClientAsync`, with its client user) and call as it
  with `ApiCallers.ClientAs`; `ReadProblemCodeAsync` also checks the problem content type.
- Commits: Conventional Commits (`feat:`, `fix:`, `test:`, `docs:`, `build:`, `ci:`, `refactor:`). Record decisions as ADRs.

## Observability

Every request has a W3C trace id: in logs, ProblemDetails `traceId`, the error page and the `X-Trace-Id` header.
Locally, logs and traces go to Seq (`Observability:OtlpEndpoint` in `appsettings.Development.json`).
Health: `/health/live` (process) and `/health/ready` (database; WSO2 from phase 2).
Troubleshooting guide: [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md).

## Gotchas

- `iam` is the identity schema name (`identity` is a T-SQL keyword).
- Queries load collections with split queries (set globally in `AddInfrastructure`, because `AsSplitQuery` is
  relational-only and Application references EF Core abstractions only). Rules have no stored order; sort them.
- `@section` is a Razor keyword: do not name a loop variable `section` in a view. The HTML encoder escapes `+`, so
  tests decode the page (`WebUtility.HtmlDecode`) before matching rule messages.
- Host tests sign in as seeded demo users by linking `AppUser.Wso2UserId` to the test subject (`BankPortal`,
  `SupervisionPortal`); without that link, use cases answer `User.NotLinked`. Roles for use cases come from the user
  record, so segregation-of-duties tests create users holding two roles (`SupervisionPortal.MultiRoleUserAsync`);
  approver clients add `amr=totp` for the approval policy.
- Host tests that depend on the date pass a `FakeTimeProvider` to `PortalHost.Create` (`WorkflowPagesTests` fixes the
  clock at 1 March 2027, so a January 2027 return is late).
- A form with several steps posts to the endpoint of the button pressed: `<button asp-action="Approve">` renders a
  `formaction`. Each endpoint still carries its own policy (`PortalEndpointMetadataTests` checks the workflow ones).
- Host saves are audited, so a host test that counts audit entries filters by action or actor; helper contexts
  (`SqlServerFixture.CreateContext`) are not audited, so test setup leaves no entries. Tamper tests disable
  `audit.TR_AuditEntries_AppendOnly`, change rows and re-enable it in a `finally`, on a database of their own.
- A user transaction around an audited save must run inside `CreateExecutionStrategy().ExecuteAsync` when retries are
  on (as in the hosts); the save joins it and holds the chain lock until it commits or rolls back.
- WSO2 rejects role names starting with `system_`; the platform admin role is `portal_admin`.
- WSO2 encrypts TOTP secrets with its own key: an administrator cannot set one. IamBootstrap enrols as the user
  (`DemoTotpStep`); never request `internal_login` from the portal, which would open WSO2's self-service APIs.
  Enrolment during sign-in is off (ADR 0020), so every user who reaches the TOTP step must be pre-enrolled.
- Configuration binding appends to a list property's initial items, so option lists default to empty and the values
  live in appsettings.json (`IamBootstrap:MfaAlwaysUsers`).
- WSO2 releases a claim only if its scope is requested and the claim is in the app's requested claims. `roles` is a
  string for one role and an array for several. Only roles whose audience is the portal app reach its tokens.
- WSO2 management API: `PUT` on an app's OIDC settings replaces everything (send `clientId` and `allowedOrigins`);
  `PATCH` with `associatedRoles` deletes roles; API resource scopes can only be added. `Wso2Applications` handles this.
- `Wso2:TrustedCaPath` is relative to the working directory. `dotnet run` starts Web SDK hosts in their project folder
  (so `src/*` use `../../.certs/regreturns-dev-ca.crt`) but console tools in the shell's folder (the repository root).
- `wso2-db-init` marks each WSO2 database complete only after all its scripts ran (`REGRETURNS_WSO2_SCHEMA`); a
  database with WSO2 tables but no marker stops the job (see the troubleshooting guide).
- EF Core cannot index complex-type columns; the unique obligation index is raw SQL in the `InitialCreate` migration.
- Never name a namespace `...Migration`: it hides EF Core's `Migration` base class in the migrations folder (CS0118).
  The legacy migration lives in `RegReturns.Infrastructure.Legacy`; only Domain and Application use `Migration`.
- `samples/legacy/*.csv` are generated (`legacy-samples`) and kept byte for byte (`-text` in `.gitattributes`); a unit
  test compares them with the generator, so regenerate rather than edit them. Seeded templates are in force from
  `DemoScenario.FormsInForceSince` (1 January 2024) so the legacy periods have a template.
- `system.migration` and the API client users have reserved user names, no WSO2 link and are refused by
  `LinkSignedInUser`, so nobody can sign in as them. `system.migration` holds no roles: `Submission.Migrate` only
  requires a regulator-side actor, and no other workflow step accepts it.
- Dapper maps reporting rows through record constructors, which need the exact SQL types (`date` is `DateTime`,
  `COUNT(*)` is `int`, enums are strings); convert to `DateOnly` and enums in `ToRow`.
- QuestPDF renders only after a licence is chosen: `ComplianceReportRenderer` sets the Community licence in its static
  constructor (ADR 0028). Render PDFs only through it.
- The Api's `Program` is referenced from integration tests through the `ApiHost` extern alias (both hosts define `Program`).
- `[Produces]` is a result filter that overwrites the content type of every object result, so problem details would go
  out as `application/json`. `ControllerBase.ValidationProblem()` does not run `InvalidModelStateResponseFactory`, so
  it lacks the `code`: use `this.InvalidRequest()`. The requested version is `HttpContext.RequestedApiVersion`.
- API pipeline order matters: Swagger UI before authentication (the fallback policy would demand a token for its
  static files), the rate limiter after authentication (to partition by client id) and before authorization.
- Docker must be running for integration tests; in a fresh cloud container start it with `sudo dockerd &`.
- The SQL Server image is pinned twice: the `sqlserver` service in `docker-compose.yml` and `SqlServerFixture.Image`.
  Dependabot only bumps the compose file, so update the fixture in the same PR (ADR 0013).
- Tests never call the real Anthropic API: unit tests give `AnthropicInsightNarrator` a stub `IHttpClientFactory`, host
  tests replace the named client's handler (`AddHttpClient(AnthropicInsightNarrator.HttpClientName)
  .ConfigurePrimaryHttpMessageHandler(...)`) and set `Ai:Anthropic:ApiKey` through `PortalHost.Create(settings: ...)`.
  Set `Ai:Anthropic:MaxRetries` to 0 there, or a 5xx is retried with back-off.
- Anthropic SDK: `AnthropicClient.BaseUrl` is init-only, and enum-like values (model, stop reason) are read with
  `.Raw()`, not `ToString()`. The named `HttpClient` has no timeout of its own; the narrator's deadline covers retries.
- `returns.ReturnInsights` references `iam.Users` with a restrict delete: deleting a user must delete their insights
  first (the demo reset keeps users and deletes every insight).
- `PortalHost` sets `Demo:ResetSchedule` empty so no host test ever runs the nightly reset; `DemoResetTests` call the
  reset directly. In .NET 10 `BackgroundService.StartAsync` runs `ExecuteAsync` on a background thread, so job tests
  wait for log 5603 and then advance the `FakeTimeProvider`; never await `ExecuteTask` (VSTHRD003).
- The seed is anchored on the last completed month and must build for any reset date: a story change that trips a
  rule on some calendar month fails `DemoDataBuilderTests.Builds_for_any_reset_date` (ten years of anchors). Add a
  justification to `DemoScenario` or move the figure; never widen a rule for the seed.
- WSO2 honours `login_hint`: the sign-in page then shows the account and keeps the user name in a hidden field, so
  browser scripts wait for the password field. WSO2's TOTP page enables Continue only on key events: type the digits
  into `pincode-1..6` with `press`, not `fill`.
- WSO2's JDBC driver connects before WSO2 loads its truststore ("trustAnchors parameter must be non-empty"), so its
  URLs pin SQL Server's certificate with `serverCertificate=` instead of trusting the CA. After `scripts/dev-certs.sh
  --force`, run `docker compose up -d --force-recreate sqlserver-tls sqlserver wso2` so the new certificate is installed
  and loaded.
- `SqlServerFixture` gives the Testcontainers SQL Server a self-signed certificate per run and the same `mssql.conf`,
  and pins it in its connection strings: tests run with forced, validated TLS like the real stack.
- The edge refuses `/scim2` and WSO2's management APIs from the internet; the portal and IamBootstrap reach them
  through the back channel (`Wso2:BackchannelAuthority`). Never route server-to-server WSO2 calls through Caddy.
- WSO2 locks an account for `IamBootstrap:AccountLockMinutes` after `FailedSignInsBeforeLock` failures in a row (the
  message is the generic "login failed"). A script or test that signs in with wrong passwords on purpose must use a
  throwaway user, not a demo user.
- Production containers drop every capability. A binary that carries a file capability (SQL Server's `sqlservr`, Seq's
  server) will not start unless that capability is added back (`cap_add: [NET_BIND_SERVICE]`): "Operation not
  permitted". An image that starts as root and relies on root overriding file permissions (Seq) runs as its own user.
- Containers read bind-mounted certificates as other users (WSO2 uid 802, the apps 1654, SQL Server 10001):
  `dev-certs.sh` keeps the folders 0755 and certificates 0644 whatever the umask, keys 0600. `COPY` copies a folder's
  contents, not the folder, so an owned empty folder needs `--chown`/`--chmod` on the `COPY` itself (the keys folder).
- WSO2 7 continues a sign-in at `/t/carbon.super/oauth2/authorize`, so Caddy's public paths include the
  `/t/carbon.super/` forms; `check-caddy.sh` tests both, and `regreturns.sh smoke` follows the portal's sign-in redirect.
- Options (the authentication handlers' too) are built once per process, and an exception while building them sticks
  until a restart: check files at start (`ValidateOnStart`, as `Wso2CertificateValidator.CanLoad` does for
  `Wso2:TrustedCaPath`) so a bad file stops the host instead of failing every request.
- Docker publishes ports around ufw: only Caddy may publish one.
- Integration test hosts watch files by polling (`PollingFileWatchers` sets `DOTNET_USE_POLLING_FILE_WATCHER` in a
  module initializer): with inotify the suite's hosts exceed Linux's default 128 instances per user and pages that
  version static files answer 500.
- Always pass an absolute `--results-directory` to `dotnet test`: the default location differs between SDK feature
  bands (under `bin/` on 10.0.1xx, the repo root on newer bands), which broke the CI coverage gate once.
- Development builds the service provider with `ValidateOnBuild`, so a service only one host provides (reports, demo
  reset, SCIM directory, database diagnostics) needs a fallback in `AddApplication` (`TryAdd` of an `Unavailable*`
  class) that Infrastructure's `Add*` method `Replace`s; `TryAdd` there would lose to the fallback registered first.
  `WebHostTests` and `ApiHostTests` build both hosts with that validation.
- axe runs through Playwright's `evaluate`, which the CSP does not block; never inject it with a script tag or turn on
  `bypassCSP`, which would hide the CSP violations the tour checks for.
