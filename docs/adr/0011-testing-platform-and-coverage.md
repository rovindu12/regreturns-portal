# 11. xUnit v3 on Microsoft Testing Platform with Microsoft code coverage

- Status: accepted (refines the plan, which named coverlet)
- Date: 2026-10-04

## Context

The .NET 10 SDK runs `dotnet test` on Microsoft Testing Platform (MTP); VSTest-only extensions such as coverlet's
collector do not run there.

## Decision

- Tests use xUnit v3 with Shouldly and NSubstitute; `global.json` opts `dotnet test` into MTP.
- Coverage uses `Microsoft.Testing.Extensions.CodeCoverage` with `coverage.config` excluding generated migrations.
- CI fails if Domain line coverage drops below 80%. An Application gate is added once Application holds business logic.
- Integration tests run against SQL Server in Testcontainers; tests that host the apps run one at a time because each host
  replaces Serilog's static bootstrap logger.

## Consequences

Contributors need the .NET 10 SDK. Coverage reports are Cobertura XML, readable by most CI tools.
