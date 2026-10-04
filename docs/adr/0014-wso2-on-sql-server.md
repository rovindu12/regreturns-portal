# 14. WSO2 Identity Server persists to the same SQL Server

- Status: accepted
- Date: 2026-10-04

## Context

WSO2 Identity Server 7.3 ships with an embedded H2 database. H2 is fine for a laptop but means a second backup and
restore story next to the application database, and WSO2 documents H2 as unsuitable for production. WSO2 supports
Microsoft SQL Server through its shipped `dbscripts/**/mssql.sql`, although its compatibility page lists only
SQL Server 2017 and 2019 as tested, and we run 2025 (ADR 0013).

## Decision

- WSO2 uses three databases on the RegReturns SQL Server instance: `WSO2_IDENTITY_DB` (identity and consent tables),
  `WSO2_SHARED_DB` (user store and registry) and `WSO2_AGENT_DB` (the 7.3 agent identity store), owned by a dedicated
  `wso2` login. The application never reads them.
- A thin image `FROM wso2/wso2is:7.3.0` adds the Microsoft JDBC driver (downloaded with a pinned SHA-256 checksum) and
  our `deployment.toml`. Every secret in that file is an `$env{...}` placeholder filled from `.env`.
- A one-off `wso2-db-init` container (built from the SQL Server image, with WSO2's scripts copied in) creates the login,
  the databases and the schema. It skips a database whose marker table already exists, so it is safe to re-run.
- The user store is `database_unique_id` (user ids are UUIDs, which become the OIDC `sub`).
- The JDBC URL uses `encrypt=true;trustServerCertificate=true` inside the Docker network for now; pinning SQL Server's
  certificate is part of the security-hardening phase.

## Consequences

One SQL Server backup covers application and identity data, which keeps the disaster-recovery runbook honest.
Verified on 2026-10-04: WSO2 7.3.0 starts cleanly on SQL Server 2025 (RTM-CU9) in about 45 seconds, and every
management call IamBootstrap makes works. If a future WSO2 or SQL Server update breaks this, the fallback is WSO2's
embedded H2 on a named volume, backed up as files.
