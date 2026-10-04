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
pushed, a summary, then **stop for the owner's go-ahead** before the next phase.

| Phase | Status |
|---|---|
| 1. Scaffold, domain, DB, seed (+ observability baseline) | done |
| 2. WSO2 in Docker, IamBootstrap, OIDC login, role policies | next |
| 3-12 | not started |

## Commands

```bash
# Prerequisites: .NET 10 SDK, Docker.
cp .env.example .env                       # then set MSSQL_SA_PASSWORD
docker compose up -d                       # SQL Server + Seq (logs/traces UI on http://localhost:8081)

# Connection string via user-secrets (never in appsettings):
dotnet user-secrets --project src/RegReturns.Web set ConnectionStrings:RegReturns "Server=localhost,1433;Database=RegReturns;User Id=sa;Password=<pw>;TrustServerCertificate=True"
# (repeat for src/RegReturns.Api and tools/RegReturns.Migrator, or export ConnectionStrings__RegReturns)

dotnet run --project tools/RegReturns.Migrator -- migrate-db --seed   # schema + demo data (idempotent)
dotnet run --project src/RegReturns.Web                               # https://localhost:7101
dotnet run --project src/RegReturns.Api                               # https://localhost:7201

dotnet build RegReturns.slnx                                   # warnings are errors
dotnet test --project tests/RegReturns.UnitTests               # fast, no Docker
dotnet test --project tests/RegReturns.IntegrationTests        # needs Docker (Testcontainers SQL Server)
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
  9xxx hosting). Never log secrets, tokens, e-mails or return figures; the redaction enricher is a safety net, not a licence.
- Time: inject `TimeProvider`; store UTC `DateTimeOffset`; dates as `DateOnly`; parse numbers with `CultureInfo.InvariantCulture`.
- Config: options classes with `ValidateDataAnnotations().ValidateOnStart()`. Secrets only in user-secrets or environment variables.
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
- EF Core cannot index complex-type columns; the unique obligation index is raw SQL in the `InitialCreate` migration.
- The Api's `Program` is referenced from integration tests through the `ApiHost` extern alias (both hosts define `Program`).
- Docker must be running for integration tests; in a fresh cloud container start it with `sudo dockerd &`.
