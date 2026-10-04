# 7. Submission values in a narrow key-value table

- Status: accepted
- Date: 2026-10-04

## Context

Each return type has different fields, and templates are configurable by administrators.

## Decision

Values are stored one row per field (`SubmissionValues`: field code, raw value as entered, parsed `decimal(19,4)`),
not as a JSON column and not as one table per return type.

## Consequences

Reporting is a plain SQL aggregate over a typed numeric column; the raw value is kept for audit and re-validation.
Templates can change without schema migrations.
