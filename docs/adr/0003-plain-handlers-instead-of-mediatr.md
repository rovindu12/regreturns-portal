# 3. Plain use-case handlers instead of MediatR

- Status: accepted
- Date: 2026-10-04

## Context

MediatR became commercially licensed in 2025. The project needs a simple, explicit way to organise use cases.

## Decision

Use cases are plain classes implementing `IQueryHandler<TQuery, TResult>` (and `ICommandHandler` from phase 3),
registered by assembly scanning in `AddApplication()`. Controllers receive the handler they need by injection.
Cross-cutting behaviour (transactions, auditing) is added with decorators or EF interceptors where needed.

## Consequences

No third-party dependency and no reflection-based dispatch. Each controller action shows exactly which use case it runs.
