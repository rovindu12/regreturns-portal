# 12. Demo data is generated through the domain workflow

- Status: accepted
- Date: 2026-10-04

## Context

The demo needs a year of believable history with deliberate anomalies (an LCR breach, a returned submission,
late filings, a missing return, an NPL spike). Hand-written SQL inserts could easily break business rules.

## Decision

`DemoDataBuilder` creates every submission by calling the same domain methods users trigger: create draft, set values,
validate, justify warnings, submit, review, return, approve. Figures are deterministic per bank and period, and history is
anchored to the current date so the nightly demo reset (phase 9) always shows the last twelve months.
All institutions and the regulator ("Bank of Valoria", currency VLD) are fictional.

## Consequences

Seeded data obeys every rule live data does; if a rule changes, seeding fails loudly. Unit tests check consistency
(totals equal components, nothing dated in the future, anomalies present).
