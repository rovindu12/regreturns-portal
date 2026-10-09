# 13. SQL Server 2025 for development, tests and hosting

- Status: accepted (replaces SQL Server 2022 in the plan)
- Date: 2026-10-04

## Context

The plan named SQL Server 2022. Dependabot proposed moving the Docker Compose service to `2025-latest`, and the owner
chose to take it. WSO2 Identity Server will keep its own databases on the same instance from phase 2, and WSO2's
environment compatibility page lists only SQL Server 2017 and 2019 as tested, so neither 2022 nor 2025 is formally
covered.

Checked before switching: `mssql/server:2025-latest` (2025 RTM-CU9, 17.0.5005.3) applies the migrations and loads the
demo data, a second run changes nothing, and all unit and integration tests pass. The image still ships `sqlcmd` under
`/opt/mssql-tools18`, so the Compose health check and the Testcontainers readiness check work unchanged. New databases
get compatibility level 170.

## Decision

- Docker Compose and the integration tests use `mcr.microsoft.com/mssql/server:2025-latest`.
- `SqlServerFixture.Image` holds the test image and must match the Compose service; Dependabot only updates Compose.
- Hosting (phase 11) uses SQL Server 2025 Express.
- Phase 2 verifies WSO2 IS persistence on 2025. If it does not work, WSO2 falls back to its embedded H2 database (the
  fallback the plan already names), recorded in a follow-up ADR.

## Consequences

The project runs on the current SQL Server release. WSO2 on 2025 is an accepted, untested combination that phase 2
must prove. A local data volume created by 2022 upgrades in place on first start and cannot be opened by 2022 again;
run `docker compose down -v` and re-seed if you need to go back.

## Update (phase 11, ADR 0034)

The image is now pinned to a cumulative update, `mcr.microsoft.com/mssql/server:2025-CU9-ubuntu-24.04` (the image
`2025-latest` pointed to when this ADR was written), in both compose files, the WSO2 database job and
`SqlServerFixture.Image`. A floating tag would let a server, the CI runner and a developer run different SQL Server
builds of the same commit. Dependabot proposes the next CU for the Dockerfiles and compose files, and `ImagePinTests`
fails until every copy matches.
