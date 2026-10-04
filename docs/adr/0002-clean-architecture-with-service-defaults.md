# 2. Clean Architecture with a shared ServiceDefaults project

- Status: accepted
- Date: 2026-10-04

## Context

The portal, the API and two console tools share business rules and the same operational concerns
(logging, tracing, health checks, error handling).

## Decision

- Projects follow Clean Architecture: `Domain` ← `Application` ← `Infrastructure` ← hosts (`Web`, `Api`, tools).
- The web portal calls Application use cases directly; it does not call the REST API. Both hosts share one set of rules.
- Cross-cutting hosting code lives in `RegReturns.ServiceDefaults` (named after the .NET Aspire convention), so every
  process logs, traces and reports health the same way.
- Architecture tests (NetArchTest) fail the build if a dependency points the wrong way or a controller touches the database.

## Consequences

One extra project beyond the brief. Layer rules are enforced by tests rather than by convention.
