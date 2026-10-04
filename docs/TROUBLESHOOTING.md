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
| `GET /health/ready` | Dependencies are reachable (database and WSO2). Returns per-check status and durations. |

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

## 5. Sign-in, tokens and WSO2

Start with the trace id, as in section 1. Denied requests also leave an `AccessDenied` or `AuthenticationFailed`
entry in the audit trail with the path and the reason (one per user, path and minute). WSO2's own log is
`docker compose logs wso2`; its management console is https://localhost:9443/console.

Never fix a certificate error by turning off TLS validation. The apps trust exactly one extra root, the development CA
from `scripts/dev-certs.sh` (ADR 0015).

| Symptom | Likely cause | Fix |
|---|---|---|
| App exits at start: `Wso2:TrustedCaPath points to a file that does not exist` | The path is relative to the app's working directory, which is the project folder under `dotnet run` | Use `../../.certs/regreturns-dev-ca.crt` for `src/*` projects; run `scripts/dev-certs.sh` if `.certs/` is missing |
| `The remote certificate is invalid ... UntrustedRoot` in the logs, `/health/ready` reports `wso2: Unhealthy` | The CA file does not match the certificate WSO2 serves (keystores rebuilt with `--force`, or a different clone's `.certs/`) | Point `Wso2:TrustedCaPath` at the CA that issued WSO2's certificate, or re-run `scripts/dev-certs.sh --force` and restart WSO2 |
| Browser warns about `https://localhost:9443` | Browsers do not trust the development CA | Import `.certs/regreturns-dev-ca.crt` into the browser's trust store for local work only |
| `/health/ready` reports `wso2: Unhealthy` right after `docker compose up` | WSO2 takes a few minutes on first start while it creates its tables | Wait for `WSO2 Carbon started` in `docker compose logs wso2`; if `wso2-db-init` failed, its log says why |
| IamBootstrap fails with `401 Unauthorized` on the management API | `WSO2_ADMIN_PASSWORD` in `.env` changed after WSO2's first start; the admin account is created once and kept in WSO2's database | Put the original password back, or change it in the Console and then in `.env` |
| WSO2 rejects a role: `Role names with the prefix: system_ are not allowed` | WSO2 reserves the `system_` prefix | Use the names in `RoleNames` (the platform admin role is `portal_admin`) |
| Signed in, but every page says access denied | The user has no role for the RegReturns Portal application (only roles whose audience is the portal app reach its tokens), or no `institution_id` for a bank role | `dotnet run --project tools/RegReturns.IamBootstrap -- demo-users` for demo users; otherwise assign the role in the Console under the portal application |
| WSO2 shows a consent page after login | Consent skipping was turned off on the portal application | Re-run `IamBootstrap apply`; it restores the application settings |
| WSO2 error page: `Callback url mismatch` or `invalid redirect_uri` | You browse the portal on a different URL than `IamBootstrap:PortalBaseUrl` (scheme, host or port) | Set `IamBootstrap:PortalBaseUrl` to the URL you use and re-run `IamBootstrap apply` |
| WSO2 error: `PKCE is mandatory` | A client sent an authorization request without `code_challenge` | The portal always sends PKCE; this means a hand-made request or a custom client |
| `invalid_grant` when returning to `/signin-oidc` | The authorization code was used twice (browser back button or a refresh) or expired | Start sign-in again; codes are single-use and short-lived |
| Sign-in loops back to WSO2 or ends with `Correlation failed` | The portal's correlation cookie was lost: plain `http`, a blocked third-party cookie, or a clock skew | Browse the portal on `https`, allow cookies for `localhost`, check the machine clock |
| Signing out in one tab does not end the session open in another (local only) | WSO2 runs in Docker and cannot reach or trust the portal's development certificate for back-channel logout | Expected locally; signing out from the portal ends both the portal and WSO2 sessions. The hosted demo (phase 11) has a trusted certificate |
| Demo user sees `Login failed` | The password was changed or the demo user was not provisioned | `IamBootstrap demo-users` resets every demo user to `DEMO_USER_PASSWORD` |
| API returns `401` with `invalid_token` and `The audience ... is invalid` in the log | The token was issued to a client without the RegReturns API audience, or an ID token was sent instead of an access token | Use a bank client created by IamBootstrap (`BANK_<CODE>_CLIENT_ID` in `.env.generated`) and send its access token |
| API returns `403` | The token lacks the scope for the endpoint, or the client is not linked to an institution | Request the scope in the token call (`scope=returns:read`); re-run `IamBootstrap apply` to relink bank clients |
| API returns `404` for another bank's data | By design: a bank cannot learn whether another bank's records exist | Use the client of the bank that owns the data |
