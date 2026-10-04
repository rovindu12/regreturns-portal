# 8. Validation rule parameters as typed columns

- Status: accepted (refines the implementation plan, which proposed a JSON parameters column)
- Date: 2026-10-04

## Context

Rules have a handful of parameter shapes: range bounds, a cross-field expression pair with a tolerance, and a
variance threshold with a comparison basis.

## Decision

`ValidationRule` stores each parameter in its own nullable, typed column (`MinValue`, `MaxValue`, `LeftExpression`,
`Operator`, `RightExpression`, `Tolerance`, `ThresholdPercent`, `VarianceBasis`). Factory methods per rule type
guarantee the right combination is set.

## Consequences

Parameters are validated by the type system and the database, and the admin screen binds directly to columns.
A new rule type with new parameters needs a migration, which is acceptable for a regulator's rulebook.
