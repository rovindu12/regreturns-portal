# Troubleshooting

How to go from "something is wrong" to the cause. This guide grows with each phase.

## 1. Find the request

Every response carries a W3C trace id:

- Users see it on the error page as **Error reference**.
- API clients get it in the `traceId` field of ProblemDetails and the `X-Trace-Id` response header.

In Seq (local: http://localhost:8081) search for it:

```
@TraceId = '4bf92f3577b34da6a3ce929d0e0e4736'
```

You get the request summary line, every log event, and the trace spans (ASP.NET Core, SQL commands, outbound HTTP)
for that request. Log events also carry `Service`, `Version` and `Environment`.

Without Seq, the same JSON lines are on stdout:

```bash
docker compose logs web | grep 4bf92f3577b34da6a3ce929d0e0e4736      # from phase 11
```

## 2. Check health

| Endpoint | Meaning |
|---|---|
| `GET /health/live` | The process is running. Does not touch dependencies. |
| `GET /health/ready` | Dependencies are reachable (database; WSO2 from phase 2). Returns per-check status and durations. |

Neither endpoint returns exception details. The cause of an unhealthy check is in the logs at the same time.

## 3. Raise a log level temporarily

Levels reload from configuration without a restart. Set an override, for example:

```bash
# environment variable form (restart required) …
Serilog__MinimumLevel__Override__Microsoft.EntityFrameworkCore.Database.Command=Information
# … or edit appsettings.json on the server; Serilog picks up the change automatically.
```

Return it to `Warning` afterwards: SQL command logs are verbose.

## 4. Common problems

| Symptom | Likely cause | Fix |
|---|---|---|
| App exits at start with `ConnectionStrings:RegReturns is not set` | No connection string configured | Set it with `dotnet user-secrets` or the `ConnectionStrings__RegReturns` environment variable |
| `/health/ready` reports `database: Unhealthy` | SQL Server down, wrong password, or firewall | `docker compose ps`, check `MSSQL_SA_PASSWORD` in `.env`, try `sqlcmd` from the host |
| `Invalid object name 'returns.Submissions'` | Migrations not applied | `dotnet run --project tools/RegReturns.Migrator -- migrate-db` |
| Home page shows zeros | Database not seeded | `dotnet run --project tools/RegReturns.Migrator -- seed` (idempotent) |
| Migrator exits with code 1 | See the `Database command failed` log event (event id 2002) for the exception | Fix the cause and re-run; migrations are transactional |
| Nothing appears in Seq | OTLP endpoint not set or Seq not running | `docker compose up -d seq`; check `Observability:OtlpEndpoint` |
| Integration tests fail with `Docker is either not running` | Testcontainers needs a Docker daemon | Start Docker (`sudo dockerd &` in a fresh Linux container) |
