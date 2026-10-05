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
| `scripts/init-env.sh` (or `dev-secrets.sh`) stops: `still the published examples and must be rotated` | `.env` was copied from `.env.example`, or kept an example value | Rotate each named value as described in [Example secrets in .env](#example-secrets-in-env) |
| `wso2-db-init` stops: `has some WSO2 tables but no completion marker` | An earlier run was interrupted while loading WSO2's schema; a half-made schema is never reused | Drop the named database (or, locally, `docker compose down -v` to start empty) and run `docker compose up -d` again |

### Example secrets in .env

`scripts/init-env.sh` creates `.env` with random values and never changes a value that is already set, because the
SQL Server volume, the keystores and WSO2's database hold those secrets. To rotate one by hand:

| Key | How to rotate |
|---|---|
| `MSSQL_SA_PASSWORD` | Run `ALTER LOGIN sa WITH PASSWORD = N'<new>'` with `sqlcmd` as `sa` (old password), put the new one in `.env`, then `docker compose up -d sqlserver` and `scripts/dev-secrets.sh` |
| `WSO2_ADMIN_PASSWORD` | Change the password in the WSO2 Console first, then in `.env` |
| `WSO2_DB_PASSWORD` | Change it in `.env`, run `docker compose run --rm wso2-db-init` (it resets the login's password), then `docker compose up -d wso2` |
| `WSO2_KEYSTORE_PASSWORD` | Change it in `.env`, run `scripts/dev-certs.sh --force`, then `docker compose up -d wso2` |
| `WSO2_AUTH_ENDPOINT_PASSWORD` | Change it and its `_SHA256` together (`printf %s '<new>' \| sha256sum`), then `docker compose up -d wso2` |
| `WSO2_ENCRYPTION_KEY` | Data WSO2 already encrypted (client secrets, TOTP secrets) becomes unreadable: drop the three `WSO2_*` databases, change the key, `docker compose up -d`, then `IamBootstrap apply` |
| `DEMO_USER_PASSWORD` | Change it in `.env`, then `IamBootstrap demo-users` |
| `AUDIT_HMAC_KEY` | Only before audit entries exist: the existing chain no longer verifies under a new key (ADR 0016) |

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
| Portal shows "Sign-in failed" with `request_expired` | The sign-in took longer than the portal's correlation window, or the browser replayed an old callback (back button) | Start sign-in again from the portal |
| Portal shows "Sign-in failed" with `idp_unreachable` | The portal could not fetch WSO2's discovery document or keys (WSO2 down or TLS trust) | Check `/health/ready` and the TLS rows above |
| Portal shows "Sign-in failed" after a successful WSO2 login; audit reason `User.UnknownInstitution` or `User.RoleRequired` | The WSO2 user has an `institution_id` RegReturns does not know, or no RegReturns role | Fix the user's institution or roles in WSO2 (for demo users: `IamBootstrap demo-users`) |
| "Sign-in failed", audit reason `User.InstitutionInactive` | The user's institution is deactivated in RegReturns | Reactivate the institution, or move the user to an active one |
| "Sign-in failed", audit reason `User.InstitutionChanged` | The token names a different institution than RegReturns has on record for this user. RegReturns never follows such a change on its own, so a user cannot switch banks by editing a claim | An administrator moves the user in RegReturns (phase 10) and in WSO2 together; for demo users run `IamBootstrap demo-users` |
| "Sign-in failed", audit reason `User.Disabled` | The RegReturns user record is disabled | Re-enable the user in RegReturns |
| "Sign-in failed", audit reason `User.IdentityConflict` | The RegReturns user is already linked to a different WSO2 user id (the WSO2 user was deleted and re-created) | Check it is the same person, then relink: clear the old `Wso2UserId` on the user record |
| "Sign-in failed", audit reason `User.SessionMissing` | WSO2's ID token has no `sid` claim, so back-channel logout could not end this session | Check that the portal application in WSO2 still has back-channel logout configured; re-run `IamBootstrap apply` |
| An approver or administrator cannot get past WSO2's TOTP step | The user has no active TOTP secret, and WSO2's enrolment during sign-in is off on purpose so nobody can enrol their own authenticator on a shared account (ADR 0020) | `IamBootstrap demo-users` re-enrols demo users and writes their secrets to `.env.generated` (`TOTP_SECRET_<USER>`); real users are enrolled by an administrator |
| WSO2 rejects a correct-looking TOTP code | The clocks of the authenticator and WSO2 differ by more than 30 seconds, or the code was already used in this 30-second step | Sync the clock; wait for the next code |
| My Account shows `authentication.flow.app.disabled` | Intended: IamBootstrap disables My Account so demo users cannot change their password or MFA (ADR 0020) | Set `IamBootstrap:LockDownSelfService=false` only for real users |
| Demo user sees `Login failed` | The password was changed or the demo user was not provisioned | `IamBootstrap demo-users` resets every demo user to `DEMO_USER_PASSWORD` |
| API returns `401` for a token that looks right, and the API log has `The signature key was not found` | The API could not fetch WSO2's signing keys, usually TLS: the log has `HttpRequestException ... SSL connection could not be established` | Fix `Wso2:TrustedCaPath` (see the first rows) and restart the API |
| API returns `401` and the API log has `The audience ... is invalid` (the response itself carries no details, by design) | The token was issued to a client without the RegReturns API audience, or an ID token was sent instead of an access token | Use a bank client created by IamBootstrap (`BANK_<CODE>_CLIENT_ID` in `.env.generated`) and send its access token |
| API returns `403` | The token lacks the scope for the endpoint, or the client is not linked to an institution | Request the scope in the token call (`scope=returns:read`); re-run `IamBootstrap apply` to relink bank clients |
| API returns `404` for another bank's data | By design: a bank cannot learn whether another bank's records exist | Use the client of the bank that owns the data |
