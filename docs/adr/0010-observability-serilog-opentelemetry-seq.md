# 10. Observability with Serilog, OpenTelemetry and Seq

- Status: accepted
- Date: 2026-10-04

## Context

Support staff must be able to go from a user's error reference to the exact request, log lines and SQL calls.

## Decision

- The W3C trace id is the single correlation id. It is on every log event, in ProblemDetails (`traceId`), on the error
  page, in the `X-Trace-Id` response header and (from phase 4) on audit entries.
- Serilog writes structured JSON to stdout and exports logs over OTLP. OpenTelemetry exports traces and metrics over OTLP
  (ASP.NET Core, HttpClient, SqlClient and `RegReturns.*` sources).
- Seq is the default OTLP backend for development and the hosted demo (free individual licence, one small container).
  Exporters are configuration only, so Grafana, Elastic or Azure Monitor are a configuration change.
- Sensitive values are redacted before any sink: properties named like passwords, tokens or e-mails, and properties
  marked `[PersonalData]`, `[SensitiveData]` or `[Secret]` (matched by attribute name so the Domain stays dependency-free).
- Health probes and the per-request summary line are kept out of normal log volume.

## Consequences

One extra container in each environment. Troubleshooting steps are documented in `docs/TROUBLESHOOTING.md`.
