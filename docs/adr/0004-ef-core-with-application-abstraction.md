# 4. EF Core for the write model, Dapper only for reporting reads

- Status: accepted
- Date: 2026-10-04

## Context

The domain model has aggregates with child collections and invariants. Dashboards need set-based aggregate queries.

## Decision

- EF Core 10 (code-first migrations) persists aggregates. Mapping uses backing fields so entities keep private setters.
- Application defines `IAppDbContext` exposing `DbSet<T>`; Application therefore references the EF Core package
  (abstractions only, no provider). This trades purity for far less repository boilerplate.
- Dapper is used only for reporting queries where hand-written SQL is clearer (phase 6).
- SQL Server `rowversion` shadow columns provide optimistic concurrency on aggregates.
- Enums are stored by name, and tables are grouped into `reference`, `iam` and `returns` schemas, so data reads clearly in SQL and audit extracts.

## Consequences

Use cases can be tested against a real SQL Server (Testcontainers) rather than mocked repositories.
