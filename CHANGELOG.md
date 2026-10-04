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
- Hash-chained, HMAC-signed audit trail with sign-in, sign-out and de-duplicated access-denied events (ADR 0016).
- Authorization policies for every role, API scopes and institution membership; MFA requirement for approvals.
- `scripts/init-env.sh` (random secrets), `scripts/dev-certs.sh` (dev CA and keystores), `scripts/dev-secrets.sh`
  (copies local secrets into user-secrets).
- Troubleshooting guide section for sign-in, tokens and WSO2.

### Changed

- SQL Server 2025 replaces 2022 for local Docker Compose and the integration tests (ADR 0013).
- The platform administrator role is `portal_admin`: WSO2 reserves the `system_` prefix.
