# 9. Versioned templates pin historical submissions

- Status: accepted
- Date: 2026-10-04

## Context

Regulators change return templates over time, but filed returns must keep rendering and validating as they were filed.

## Decision

Fields and rules belong to a `TemplateVersion` (Draft → Published → Retired). Published versions are immutable.
Each submission records the version it was captured with.

## Consequences

Changing a template means publishing a new version. Old submissions are never re-interpreted against new rules.
