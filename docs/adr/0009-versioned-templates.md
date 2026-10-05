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

## Amendment (2026-10-05, phase 3): lifecycle rules

- `EffectiveFrom` is the first reporting-period start date a version applies to. The version used for a period is the
  published one with the latest `EffectiveFrom` on or before the period start (the higher version wins a tie); retired
  versions are never chosen. A submission keeps the version it was started with.
- Each return type has at most one draft version. A new draft copies the requested version (or the latest published
  one) with all its fields and rules, so administrators edit by difference.
- Drafts can be edited and deleted. Publishing re-checks every rule against the fields. A field used by a rule cannot be
  removed until the rule is.
- Retiring is an explicit step on a published version and does not touch submissions captured with it.
