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
| 2. WSO2 in Docker, IamBootstrap, OIDC login, role policies | in progress |
| 3-12 | not started |

## Commands

```bash
# Prerequisites: .NET 10 SDK, Docker, openssl, jq. Run everything from the repository root.
scripts/init-env.sh                        # .env with random secrets (git-ignored)
scripts/dev-certs.sh                       # dev CA + WSO2 keystores in .certs/ (ADR 0015)
docker compose up -d                       # SQL Server, WSO2 (https://localhost:9443/console), Seq (http://localhost:8081)
scripts/dev-secrets.sh                     # copies .env (and .env.generated) into user-secrets for every project

dotnet run --project tools/RegReturns.Migrator -- migrate-db --seed   # schema + demo data (idempotent)
dotnet run --project tools/RegReturns.IamBootstrap -- apply           # WSO2 claims, API, apps, roles, bank clients,
                                                                      # demo users; writes .env.generated (idempotent)
scripts/dev-secrets.sh                                                # again, to pick up the portal client secret
dotnet run --project tools/RegReturns.IamBootstrap -- demo-users      # reset demo users only (passwords, roles)
dotnet run --project src/RegReturns.Web                               # https://localhost:7101
dotnet run --project src/RegReturns.Api                               # https://localhost:7201

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
src/RegReturns.Infrastructure  EF Core context, configurations, migrations, seeding, DI.
src/RegReturns.ServiceDefaults Serilog, OpenTelemetry, health endpoints, ProblemDetails, exception handler.
src/RegReturns.Web             MVC portal (Razor + Bootstrap 5).
src/RegReturns.Api             REST API (versioned from phase 5).
tools/RegReturns.Migrator      `migrate-db [--seed]`, `seed`; legacy migration from phase 7.
tools/RegReturns.IamBootstrap  `apply`, `demo-users`: idempotent WSO2 setup over its REST APIs (ADR 0017).
tests/RegReturns.UnitTests     Domain, seeding, redaction and architecture tests.
tests/RegReturns.IntegrationTests  Testcontainers SQL Server, WebApplicationFactory host tests.
```

Dependency rule: Domain ← Application ← Infrastructure ← hosts. Enforced by `tests/RegReturns.UnitTests/Architecture`.

Key domain rules (see `src/RegReturns.Domain/Submissions/Submission.cs`):
- Workflow: Draft → Submitted → UnderReview → ReturnedForCorrection / Approved / Rejected (`SubmissionWorkflow`).
- Maker creates and edits; checker submits and must not be the preparer or last editor; reviewer picks up;
  approver approves or rejects and must not be the reviewer. Supervisory steps require regulator staff (no institution).
- Submit requires values, a validation run after the last edit, no errors and a justification (≥20 chars) per warning.
- Comments are required on every step except "start review". Returning for correction bumps `Revision`.
- `IsLate` is set on first submission if after the obligation's due date (UTC).

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
- Logging: `[LoggerMessage]` source-generated methods with event ids grouped per area (1xxx persistence, 2xxx migrator,
  30xx audit and identity, 31xx portal sign-in, 32xx API authentication, 4xxx IamBootstrap, 9xxx hosting). Never log secrets, tokens, e-mails or return figures; the redaction enricher is a safety net, not a licence.
- Time: inject `TimeProvider`; store UTC `DateTimeOffset`; dates as `DateOnly`; parse numbers with `CultureInfo.InvariantCulture`.
- Config: options classes with `ValidateDataAnnotations().ValidateOnStart()`. Secrets only in user-secrets or environment variables.
  Secret keys: `ConnectionStrings:RegReturns`, `Audit:HmacKey` (Web and Api share it), `Oidc:ClientSecret` (Web).
  WSO2 settings: `Wso2:Authority` (public issuer base), `Wso2:TrustedCaPath`, optional `Wso2:BackchannelAuthority`
  (e.g. `https://wso2:9443/` inside Docker), `Iam:EnforceMfa`.
  IamBootstrap reads `.env` itself and writes generated client secrets to `.env.generated` (mode 600, git-ignored).
- Identity: WSO2 is the only identity store; `AppUser` rows link to WSO2 by subject id. Role, claim and scope names are
  constants in `RoleNames`, `ClaimNames`, `ApiScopes` and `IamNames`. Change WSO2 only through IamBootstrap steps, never
  by hand in the Console, so a fresh environment can be rebuilt.
- Tests: `Method_or_behaviour_in_plain_words` names, Shouldly assertions, one behaviour per test. Host tests join the
  `HostedAppsDefinition` collection. Integration tests that write data use `SqlServerFixture.NewDatabaseConnectionString()`.
- Commits: Conventional Commits (`feat:`, `fix:`, `test:`, `docs:`, `build:`, `ci:`, `refactor:`). Record decisions as ADRs.

## Observability

Every request has a W3C trace id: in logs, ProblemDetails `traceId`, the error page and the `X-Trace-Id` header.
Locally, logs and traces go to Seq (`Observability:OtlpEndpoint` in `appsettings.Development.json`).
Health: `/health/live` (process) and `/health/ready` (database; WSO2 from phase 2).
Troubleshooting guide: [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md).

## Gotchas

- `iam` is the identity schema name (`identity` is a T-SQL keyword).
- WSO2 rejects role names starting with `system_`; the platform admin role is `portal_admin`.
- WSO2 releases a claim only if its scope is requested and the claim is in the app's requested claims. `roles` is a
  string for one role and an array for several. Only roles whose audience is the portal app reach its tokens.
- WSO2 management API: `PUT` on an app's OIDC settings replaces everything (send `clientId` and `allowedOrigins`);
  `PATCH` with `associatedRoles` deletes roles; API resource scopes can only be added. `Wso2Applications` handles this.
- `Wso2:TrustedCaPath` is relative to the working directory. `dotnet run` starts Web SDK hosts in their project folder
  (so `src/*` use `../../.certs/regreturns-dev-ca.crt`) but console tools in the shell's folder (the repository root).
- EF Core cannot index complex-type columns; the unique obligation index is raw SQL in the `InitialCreate` migration.
- The Api's `Program` is referenced from integration tests through the `ApiHost` extern alias (both hosts define `Program`).
- Docker must be running for integration tests; in a fresh cloud container start it with `sudo dockerd &`.
- The SQL Server image is pinned twice: the `sqlserver` service in `docker-compose.yml` and `SqlServerFixture.Image`.
  Dependabot only bumps the compose file, so update the fixture in the same PR (ADR 0013).
- Always pass an absolute `--results-directory` to `dotnet test`: the default location differs between SDK feature
  bands (under `bin/` on 10.0.1xx, the repo root on newer bands), which broke the CI coverage gate once.
