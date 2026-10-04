# 5. Domain-assigned version 7 GUID identifiers

- Status: accepted
- Date: 2026-10-04

## Context

Aggregates reference each other (a submission references an obligation and a template) and segregation-of-duties
rules compare user ids, often before anything is saved. Sequential integers leak record counts through the API.

## Decision

Every entity receives a time-ordered `Guid.CreateVersion7()` (RFC 9562) in the `Entity` base constructor.
EF Core is told never to generate keys.

## Consequences

- Objects are fully formed in memory; the seed data and unit tests need no database to wire relationships.
- SQL Server orders `uniqueidentifier` by its last bytes, so v7 GUIDs are not perfectly sequential for clustered indexes.
  At this system's volumes the page-split cost is negligible; if volumes grow, the clustered index can move to a
  date column without changing the domain.
