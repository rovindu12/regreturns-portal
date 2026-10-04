# 6. Result type for expected business failures

- Status: accepted
- Date: 2026-10-04

## Context

Most workflow failures are expected (wrong role, unjustified warning, checker is the maker). Exceptions for these
make control flow hard to follow and are expensive.

## Decision

Domain methods return `Result` / `Result<T>` carrying a stable `Error(Code, Message)`. Error codes such as
`Submission.CheckerIsMaker` are part of the contract: the API maps them to ProblemDetails and tests assert on them.
`DomainException` is reserved for programming errors (constructing an invalid value object).

## Consequences

Callers must check results; analyzers and tests make ignored failures visible.
