# Architecture Decision Records

| # | Decision |
|---|----------|
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions |
| [0002](0002-clean-architecture-with-service-defaults.md) | Clean Architecture with a shared ServiceDefaults project |
| [0003](0003-plain-handlers-instead-of-mediatr.md) | Plain use-case handlers instead of MediatR |
| [0004](0004-ef-core-with-application-abstraction.md) | EF Core for the write model, Dapper only for reporting reads |
| [0005](0005-domain-assigned-guid-v7-keys.md) | Domain-assigned version 7 GUID identifiers |
| [0006](0006-result-type-for-expected-failures.md) | Result type for expected business failures |
| [0007](0007-submission-values-narrow-table.md) | Submission values in a narrow key-value table |
| [0008](0008-typed-validation-rule-parameters.md) | Validation rule parameters as typed columns |
| [0009](0009-versioned-templates.md) | Versioned templates pin historical submissions |
| [0010](0010-observability-serilog-opentelemetry-seq.md) | Observability with Serilog, OpenTelemetry and Seq |
| [0011](0011-testing-platform-and-coverage.md) | xUnit v3 on Microsoft Testing Platform with Microsoft code coverage |
| [0012](0012-seed-data-through-the-domain.md) | Demo data is generated through the domain workflow |
| [0013](0013-sql-server-2025.md) | SQL Server 2025 for development, tests and hosting |
| [0014](0014-wso2-on-sql-server.md) | WSO2 Identity Server persists to the same SQL Server |
| [0015](0015-explicit-trust-for-the-dev-ca.md) | Explicit trust for a private development CA, never disabled validation |
| [0016](0016-hash-chained-audit-trail.md) | Tamper-evident audit trail as an HMAC hash chain |
| [0017](0017-iam-bootstrap.md) | WSO2 configuration as code with an idempotent setup tool |
| [0018](0018-api-token-validation-and-institution-scoping.md) | API token validation and institution scoping |
| [0019](0019-portal-sign-in-and-sessions.md) | Portal sign-in and session lifetime |
| [0020](0020-demo-mfa-and-self-service-lockdown.md) | Demo TOTP enrolment and self-service lockdown |
| [0021](0021-in-house-rule-expressions.md) | A small in-house expression language for cross-field rules |
| [0022](0022-safe-return-file-uploads.md) | Return files: checked by content, stored in the database, never served |
| [0023](0023-one-live-return-per-obligation.md) | One live return per obligation, saved with optimistic concurrency |
| [0024](0024-auditing-data-changes.md) | Every saved data change joins the audit chain in the same transaction |
| [0025](0025-workflow-steps-and-supervision-visibility.md) | Workflow steps through one command; supervisors see returns once submitted |
| [0026](0026-api-delivers-drafts-through-a-client-user.md) | Bank systems deliver returns through the API as makers; people submit them |
| [0027](0027-web-api-v1-conventions.md) | Web API v1 conventions: versions, OpenAPI, errors, paging, idempotency and rate limits |
| [0028](0028-reporting-views-and-exports.md) | Reporting views read with Dapper, and audited Excel and PDF exports |
| [0029](0029-legacy-data-migration.md) | Legacy data migration: mapping file, one reconciled transaction, approved returns through a system account |
| [0030](0030-advisory-return-insights.md) | Advisory return insights: figures-only payload, Claude with a rule-based fallback, every generation audited |
| [0031](0031-public-demo-and-demo-reset.md) | Public demo pages, and a demo reset that replaces the workload but keeps the directory and the audit chain |
| [0032](0032-administrator-opened-totp-enrolment.md) | Administrators open a TOTP enrolment window; the person enrols at their next sign-in |
| [0033](0033-security-hardening.md) | Security hardening: browser policies, account lock, strict TLS to SQL Server, a deny-by-default edge and diagnostics |
