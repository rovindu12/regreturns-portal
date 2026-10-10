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
| `/health/ready` reports `database: Unhealthy` | SQL Server down, wrong password, or firewall | `docker compose ps`, check `MSSQL_SA_PASSWORD` in `.env`, try `sqlcmd -S localhost -U sa -Ns -J .certs/sqlserver/mssql.crt` from the host (password in `SQLCMDPASSWORD`) |
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
| `MSSQL_SA_PASSWORD` | Run `ALTER LOGIN sa WITH PASSWORD = N'<new>'` with `sqlcmd -Ns -J .certs/sqlserver/mssql.crt` as `sa` (old password), put the new one in `.env`, then `docker compose up -d sqlserver` and `scripts/dev-secrets.sh` |
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
| Signed in, but a page says *You do not have access* | The user's roles do not include that area (the menu shows only what their roles open). Only roles whose audience is the portal application reach its tokens; a user with no portal role at all is refused at sign-in (`User.RoleRequired`, below) | For demo users run `dotnet run --project tools/RegReturns.IamBootstrap -- demo-users`; for other people, add the portal application's role to the user in WSO2 (role membership is WSO2 data; its configuration is changed only by IamBootstrap) |
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
| "Sign-in failed", audit reason `User.InstitutionChanged` | The token names a different institution than RegReturns has on record for this user. RegReturns never follows such a change on its own, so a user cannot switch banks by editing a claim | If the claim is wrong, correct `institution_id` in WSO2. The portal has no way to move a person between banks: someone who joins another bank gets a new account. For demo users run `IamBootstrap demo-users` |
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

## 6. Returns, templates and uploads

Use cases return stable error codes (`Upload.ContentMismatch`, `Submission.EditConflict`, ...). Refused uploads and
edit conflicts are logged with the code, so search Seq by code or by event id:

```
EventId.Id = 5202 and ErrorCode = 'Upload.MacrosNotAllowed'
```

| Event id | Event |
|---|---|
| 5001-5004 | Template draft started, deleted, published, retired |
| 5101 | Return draft started (with its source: web, upload, API) |
| 5102 | Return values saved |
| 5103 | Return validated (error and warning counts) |
| 5104 | Warning justified |
| 5105 | Edit refused: the page was older than the saved return |
| 5201 | Upload accepted (format, size, fields loaded, stored file id) |
| 5202 | Upload refused (error code and size) |
| 5210 | A workbook could not be read (exception type only); refused as `Upload.Unreadable` |

Logs never contain figures, file names or justification text. Metrics: `regreturns.validation.runs` (by outcome),
`regreturns.validation.findings` (by rule code and severity) and `regreturns.uploads` (by outcome and reason); the
`returns.validate` span times each validation run.

| Symptom | Likely cause | Fix |
|---|---|---|
| Bank page says `Your sign-in is not linked to an active RegReturns user` (`User.NotLinked`) | The session's WSO2 subject matches no active user record: the user was disabled, or relinked while signed in | Sign out and in again; if it persists, check the user's `Wso2UserId` and status |
| "Someone else saved this return after you opened it" (`Submission.EditConflict`, event 5105) | Another maker saved the same draft first (ADR 0023) | Reload to see their values, or save again to replace them |
| `Common.Conflict` on save or upload | Two saves of the same return raced in the same instant | Reload and save again |
| Start or upload says no template applies (`Template.NoApplicableVersion`) | No published version of the return type has `EffectiveFrom` on or before the period start | Publish a version for that period in Administration, Templates (ADR 0009) |
| Start says the obligation is closed (`Submission.ObligationClosed`) | A return for the period is already approved | Nothing to file; corrections to approved returns are out of scope |
| Upload refused: `Upload.FileType` | The name does not end in `.xlsx` or `.csv` (`.xls` and `.xlsm` are not accepted) | Save as Excel Workbook (`.xlsx`) or CSV UTF-8 |
| Upload refused: `Upload.ContentMismatch` | The content does not match the extension, for example a renamed `.xls`, a PDF or a CSV saved with an `.xlsx` name | Download the template again and save it in its own format |
| Upload refused: `Upload.MacrosNotAllowed` | The workbook contains a VBA project | Save as `.xlsx` (Excel removes the macros) |
| Upload refused: `Upload.NoHeader` or `Upload.NoValues` | The first sheet has no `FieldCode` and `Value` header row, or no field rows under it | Start from the downloaded template; keep its header row and the first sheet |
| Upload refused: `Upload.UnknownFields` | The file names field codes the return's template version does not have (often a template from another return type or an older version) | Download the template for this obligation; the message lists up to ten unknown codes |
| Upload refused: `Upload.DuplicateField` | A field code appears on two rows | Keep one row per field |
| Upload refused: `Upload.Unreadable` | The file is damaged, is a zip bomb (over 200 entries, 50 MB expanded or a compression ratio over 100), has more than 2,000 rows, a cell over 4,000 characters, a CSV line over 200 columns or broken quoting; the message names the line where it can | Start from the downloaded template; event 5210 gives the exception type for workbooks |
| Upload refused: `Upload.TooLarge`, or the browser shows a 413 / connection reset | The file is over 5 MB (the server cuts requests a little above that) | Remove other sheets and formatting; a return template is a few KB |
| A value from a spreadsheet shows as invalid although Excel displays it correctly | Excel shows a formatted number, but the cell holds text such as `1.234,56`, or more decimals than the field allows | Enter plain numbers with a full stop for decimals; the hint under each field gives the allowed decimals |
| A variance warning appears for the first return of a new bank, or never appears | Variance rules compare with the last **approved** return for the previous period or the same period last year; without one, or when it was zero, they are skipped | Expected; check the prior return's status in Supervision |
| A rule cannot be added to a draft template (`Template.InvalidExpression`) | The expression uses an unknown function, a field outside brackets or a comma as decimal separator | Allowed: `[CODE]`, numbers with a full stop, `+ - * /`, parentheses, `Min`, `Max`, `Abs` (ADR 0021) |
| A field cannot be removed from a draft template (`Template.FieldInUse`) | Rules still refer to it; the message names them | Remove or change those rules first |

## 7. Workflow and supervision

Every workflow step is logged: 5301 when it happens (action, from and to status, revision, late flag) and 5302 when
it is refused, with the error code. Search Seq for one return or one kind of refusal:

```
EventId.Id in [5301, 5302] and SubmissionId = '...'
EventId.Id = 5302 and ErrorCode = 'Submission.CheckerIsMaker'
```

Comments are never logged; they are in the return's history and the audit trail. The
`regreturns.workflow.transitions` metric counts steps by action and outcome (with the error code when refused),
`regreturns.workflow.late_submissions` counts returns first submitted after their due date, and the
`returns.transition` span times each step.

| Symptom | Likely cause | Fix |
|---|---|---|
| A checker sees "You prepared or last edited this return, so another checker must submit it" (`Submission.CheckerIsMaker`) | The user holds both bank roles and created or last changed the values | Another checker submits it; segregation of duties is enforced on purpose |
| Submit is greyed out or refused with `Submission.ValidationOutdated`, `HasErrors` or `UnjustifiedWarnings` | Values changed after the last validation, an error is open, or a warning has no justification | Validate again, fix the errors and justify each warning (at least 20 characters) |
| "A comment is required for this step" (`Submission.CommentRequired`) | Submitting, returning, approving and rejecting all need a comment; only starting a review does not | Write a comment (up to 2,000 characters) |
| An approver sees "Approving or rejecting needs a sign-in with your authenticator app", or approve answers 403 | The session's `amr` claim has no TOTP method and `Iam:EnforceMfa` is on (the `Supervision.Approve` policy) | Sign out and in again; WSO2 asks every approver for TOTP (ADR 0020). Check the access-denied entry in the audit trail |
| "You reviewed this return, so another approver must decide it" (`Submission.ApproverIsReviewer`) | The approver also holds the reviewer role and picked the return up | Another approver decides it |
| `Submission.InvalidTransition` | The return moved on in another tab or by another user, for example it was already picked up or decided | Reload the page to see its status and history |
| `Common.Conflict` on a workflow step | Two people acted on the same return in the same instant; the second save lost on the row version | Reload; the first step stands |
| A supervisor gets 404 for a return the bank can see | The bank has not submitted it yet: drafts are visible only to their bank (ADR 0025) | Expected; it appears in the worklist once a checker submits it |
| A return is missing from "Decided in the last 30 days" | Only decisions of the last 30 days are listed, newest 50 | Open it from the bank's history or filter the worklist by bank |


## 8. Audit trail

Every save in the portal and the API writes one audit entry per changed aggregate in the same transaction (ADR 0024),
so a change that is in the database has its entry in the trail, and a failed or rolled-back save has none. Auditors and
administrators open **Audit trail**: filter by action, entity type and id, or actor, and select an entity id to see
that entity's whole history. Each data-change entry lists the values before and after; the trace id links it to the
request's logs in Seq.

**Verify chain** checks every entry up to the newest one and records the check as a `Chain verified` entry. It logs
3003 when the chain is intact, with the newest entry's sequence and hash, and 3004 when it is broken. The
`audit.verify-chain` span carries the entries checked and the kind of break; `audit.append-data-changes` times the
audited part of each save.

```
EventId.Id in [3003, 3004]
EventId.Id = 3004
```

| Result or symptom | What it means | What to do |
|---|---|---|
| "Entry N is missing: entry M follows entry N-1" | Entries were deleted from the database with the append-only trigger disabled | Treat it as a security incident: keep the database as it is, find who disabled `audit.TR_AuditEntries_AppendOnly` (SQL Server audit, DBA access records) and compare with a backup |
| "Entry N does not carry the hash of entry N-1" | Entries were re-ordered, or one was replaced by an entry sealed for another position | As above |
| "Entry N does not match its hash" | Entry N, or its change document, was edited after it was written | As above; the entry's values in a backup show what was changed |
| "Entry 1 does not match its hash" (or the first entry one host wrote) straight after a deployment | `Audit:HmacKey` is not the key the entries were written with, or the Web and Api hosts have different keys | Restore the original key in both hosts; never rotate it while entries exist (ADR 0016) |
| The chain is intact but its newest entry is older than a head logged earlier (3003) | Entries were deleted from the end of the chain, which leaves no gap to detect (ADR 0024) | Compare the heads recorded by earlier verifications; investigate as above |
| Saves fail with SQL error 51000 "Could not lock the audit chain." | A transaction held the chain lock for more than 10 seconds, so the writer gave up | Look for a long-running transaction in the logs at that time (a slow request that saved, or a session left open in SQL Server) |
| Error 51001 from SQL Server | Something tried to update or delete an audit entry | Expected: the trail is append-only |
| A change is not in the trail | It was made outside EF Core (raw SQL, the migrator and seeding), or it only touched properties that are never recorded (`AppUser.Email`, `StoredFile.Content`) | Expected; seeding writes no entries. Data fixed by hand in SQL is not audited, so avoid it |

## 9. Web API

Bank systems call the API with a WSO2 client-credentials token (ADR 0018) and deliver returns as drafts (ADR 0026).
Every error is `application/problem+json` with a stable `code` and the `traceId` to search for in Seq (ADR 0027).
Swagger UI is at `/swagger` and the OpenAPI document at `/openapi/v1.json`; neither needs a token.

```
EventId.Id in [5106, 5107]                         -- deliveries and refused deliveries
EventId.Id in [3301, 3302, 3303, 3306]             -- idempotent replays, reused keys, retries in flight, takeovers
EventId.Id = 3304                                  -- rate limit reached (Partition = client:<id> or ip:<address>)
EventId.Id in [3305, 3307]                         -- purge of expired idempotency records
```

Values of a return are never logged. The metrics `regreturns.api.deliveries` (outcome created, updated or refused,
with the error code), `regreturns.api.idempotent_requests` (outcome started, replayed, key_reused, in_progress) and
`regreturns.api.rate_limited` show the same at a glance.

| Status and `code` | Likely cause | Fix |
|---|---|---|
| 401 | No token, an expired one, or one from another issuer or for another audience | Get a new client-credentials token from WSO2; section 5 covers token checks |
| 403 `User.NotLinked` | The client is registered and active but has no active client user | Re-run `IamBootstrap apply`, which creates the client user |
| 403 without a code | The token lacks the scope (`returns:submit` to deliver, `returns:read` to read returns, `reference:read` for return types), or the client or its bank is not active in `iam.ApiClients` | Ask WSO2 for the scope (the public demo client is read-only on purpose); check `iam.ApiClients.IsActive` and the institution |
| 400 `Idempotency.KeyRequired` or `Idempotency.KeyInvalid` | A `POST` without an `Idempotency-Key`, or with spaces or non-ASCII characters in it | Send a new UUID per delivery, and the same one when retrying it |
| 422 `Idempotency.KeyReused` | The key was used in the last 24 hours for a different body or path | Use a new key for a new delivery; reuse a key only to retry the identical request |
| 409 `Idempotency.InProgress` with `Retry-After: 2` | The first request with this key is still running, or stopped less than a minute ago | Retry after the delay; an abandoned claim is taken over after `Api:Idempotency:InFlightSeconds` (log 3306) |
| A retry answers the first reply again, with `Idempotent-Replayed: true` | Expected: the delivery already happened | Read the stored answer; 403, 409, 429 and 5xx answers are never stored, so those retries run again |
| 400 `Request.Invalid` | Malformed period, unknown status, page size over 100, or a value that is an object or array (`errors` names the field, such as `values.LCR`) | Fix the request; periods look like `2027-03` or `2027-Q1` |
| 422 `Delivery.NoObligation` or `ReturnType.PeriodMismatch` | The bank owes no such return for the period, or a quarter was sent for a monthly return | Check `GET /v1/return-types` for the frequency and the bank's obligations in the portal |
| 422 `Delivery.UnknownFields` | A field code is not in the template version for that period | Read `GET /v1/return-types/{code}/template?period=...`; codes are case-sensitive |
| 409 `Submission.NotEditable` | The return was already submitted, is under review or was decided | Wait until the regulator returns it for correction, then deliver again |
| 409 `Delivery.Concurrent` | Someone saved the same return (in the portal or with another key) at the same moment | Retry with a new key after reading the return |
| 404 `Submission.NotFound` | Unknown id, or a return of another bank (they look the same on purpose) | Use ids from `GET /v1/submissions` |
| 429 `RateLimit.Exceeded` with `Retry-After` | More than `Api:RateLimit:PermitLimit` requests in the window from one client (or one IP without a token) | Back off for `Retry-After` seconds; raise the limit only for a known batch job |
| 413 `Request.TooLarge` | The request body is over 1 MiB | A return is far smaller; check what the client sends |
| Swagger UI "Authorize" fails with a CORS or `invalid_client` error | The API's origin is not on the demo client's allowed origins, or the secret is wrong | Set `IamBootstrap:ApiBaseUrl` to the API address and re-run `apply`; the demo secret is in `.env.generated` |

## 10. Reports and exports

The reports area reads the `reporting` views with Dapper (ADR 0028). Its queries show in traces as SqlClient spans
under the request; exports log one event each, never with figures.

```
EventId.Id = 5401                                  -- report exported (report, return type, format, size, time)
```

Who exported what, when and from where is in the audit trail: filter the auditor screen by the action "Report
exported", or query `audit.AuditEntries` for `Action = N'ReportExported'`.

| Symptom | Likely cause | Fix |
|---|---|---|
| The page says "No return types are set up yet." | No active return type | Run `migrate-db --seed`, or publish a template and activate its return type |
| 404 on `/reports?returnType=...` | Unknown or inactive return type code | Use a code from the tabs (`MLR`, `MDA`, `QCAR`); case does not matter |
| 400 on `/reports/compliance/export` | `format` is missing or not `xlsx` or `pdf` | Use the download buttons, which send the format |
| `Invalid object name 'reporting.ObligationCompliance'` | The database predates the `AddReportingViews` migration | Run `migrate-db` |
| `Invalid column name ...` from a reporting view after a migration | A migration renamed a column the views use | Recreate the view in the same migration; the read model's integration tests catch this |
| A bank's current return shows "Not due yet" to a supervisor, though the bank has a draft | Expected: regulator staff never see a draft (ADR 0025) | None; it shows once the bank submits |
| A key ratio is missing | Not listed in `Reports:KeyRatios`, or no published template has the field | Add `{ "ReturnType": "MLR", "Field": "LCR" }`; a field only appears once a template has it |
| A key ratio has no line for a bank | No approved return of that bank in the window | Expected; sparklines use approved returns only |
| Charts are blank but tables show figures | `chart.umd.min.js` or `reports.js` did not load (check the browser console) | Restore `wwwroot/lib/chart.js`; the page stays readable without charts |
| PDF export fails with a QuestPDF licence exception | The licence was not set before rendering | It is set in `ComplianceReportRenderer`'s static constructor; render only through that class |
| PDF export fails with `DllNotFoundException` for QuestPDF's native library | A runtime without a matching native build (for example an unusual Linux distribution) | Run on a glibc or musl x64/arm64 image; QuestPDF ships those builds |

## 11. Legacy data migration

`regreturns-migrator legacy` (ADR 0029, [data migration guide](DATA-MIGRATION.md)) logs each run under the trace of
its `migration.legacy` activity, never with return figures:

```
EventId.Id = 2101                                  -- started (files, source folder, dry run)
EventId.Id = 2102                                  -- file read (name, rows, SHA-256)
EventId.Id = 2103                                  -- finished (run id, outcome, counts, committed)
EventId.Id = 2104                                  -- did not reconcile; nothing committed (warning)
EventId.Id = 2105                                  -- could not start (error code and reason)
EventId.Id = 2106                                  -- failed with an exception; nothing committed (critical)
```

Every run, including dry runs and runs that did not reconcile, is a row in `migration.Runs`, with its files in
`migration.RunFiles` and row errors in `migration.RowErrors`. Rejected rows are expected and do not change the exit
code; the row error codes are explained in the data migration guide.

| Symptom | Likely cause | Fix |
|---|---|---|
| Exit code 1, `Migration.SourceNotFound` | Wrong `--source` or no `mapping.json` in it | Check the path; it is relative to the shell's folder. Pass `--mapping` for a mapping kept elsewhere |
| Exit code 1, `Migration.MappingInvalid` | Malformed JSON, an unknown member, a name mapped to two banks, a column named twice, an unknown return type or field code | Fix what the message names; every problem is listed |
| Exit code 1, `Migration.SourceMismatch` | A `.csv` file in the folder is not in the mapping (or the other way round), or a header column is neither mapped nor ignored | Map or remove the file; add the column to `fields` or `ignoredColumns` |
| Exit code 1, `Migration.FileUnreadable` | Not UTF-8 text, or larger than 64 MB | Re-export as UTF-8 CSV; split a larger export |
| Exit code 1 at start-up, `Audit:HmacKey` validation error | The migrator has no audit key | Run `scripts/dev-secrets.sh`, or set `Audit__HmacKey` to the key the portal uses |
| Exit code 2, "did not reconcile" | A stored value differs from the source: a re-export with changed figures for a period already migrated, or a value changed in the database since | Compare `reconciliation-detail.csv` (status `Mismatch`); correct history in the portal, not by rerunning |
| Every row of a file is `Legacy.BadDate` | The file's date style is not in `dateFormats` | Add the format; for `05/06/2024`, list the day-first or month-first format you mean first |
| Many `Legacy.UnknownInstitution` rows | A bank's spelling is missing from the mapping | Add it under the bank code in `institutions` |
| A row is `Legacy.AlreadyFiled` | The portal already has a return filed there for the period | Expected: portal returns are never replaced |
| Rows from before 2024 are `Legacy.NoTemplate` | No template is in force for the period | Expected for the samples; publish a template for the period if that history is needed |
| A migrated return shows warnings | VRRS accepted them; the migration keeps them with a fixed justification | None |
| `SqlException` 1205 (deadlock) or a timeout during a large run | Other writers hold locks the load needs | Run outside working hours; the run is one transaction and rolls back cleanly |

## 12. Advisory insights

Insights on the review page (ADR 0030, [guide](AI-ASSISTANT.md)) log under the request's trace, in an
`insights.generate` activity, and never log figures, the payload, the answer or the API key:

```
EventId.Id = 5501                                  -- generated (insight, submission, revision, writer, model, ms, audit entry)
EventId.Id = 5502                                  -- reused: nothing changed since the last insight
EventId.Id = 5503                                  -- payload failed the privacy guard; nothing sent (error)
EventId.Id = 5504                                  -- provider gave no answer; fixed rules wrote the insight (warning)
EventId.Id = 5505                                  -- reviewer gave up while the provider was answering; audited (warning)
EventId.Id = 5506                                  -- request refused (error code)
EventId.Id = 5511                                  -- Anthropic answered (model, ms, input and output tokens, stop reason)
EventId.Id = 5512                                  -- Anthropic call failed (error code, HTTP status, exception type)
EventId.Id = 5513                                  -- no answer within Ai:Anthropic:TimeoutSeconds
EventId.Id = 5514                                  -- answer unusable (refusal, cut short, not matching the schema)
EventId.Id = 5515                                  -- no API key configured
```

The SDK's HTTP call shows as an `HTTP POST` span to `api.anthropic.com` under the request. Every generation is an
`InsightGenerated` audit entry; the panel names its number.

| Symptom | Likely cause | Fix |
|---|---|---|
| Every insight says "No API key is configured" | `Ai:Anthropic:ApiKey` is empty | Add `ANTHROPIC_API_KEY` to `.env` and run `scripts/dev-secrets.sh`, or set `Ai__Anthropic__ApiKey` |
| "The AI provider did not accept the API key" (5512, HTTP 401 or 403) | Wrong, revoked or unfunded key | Create a key in the Anthropic Console and set it again |
| "The AI provider rejected the request" (5512, HTTP 400 or 404) | `Ai:Anthropic:Model` names a model the key cannot use, or an invalid setting | Check the model id; the 5512 entry has the status |
| "The AI provider's rate limit was reached" (HTTP 429) | Too many requests or tokens for the key's tier | Wait and press the button again; lower `Effort` or `MaxOutputTokens` |
| "The AI provider did not answer in time" (5513) | Slow answer at high effort, or the network | Raise `Ai:Anthropic:TimeoutSeconds` (at most 300) or lower `Effort` |
| "The AI provider could not be reached" (5512, `HttpRequestException` or HTTP 5xx/529) | No outbound HTTPS, a proxy, or an overloaded API | Check egress to `api.anthropic.com:443`; set `Ai:Anthropic:BaseUrl` for a proxy |
| "The AI provider's answer could not be used" (5514) | The answer was cut short (`max_tokens`) or did not match the schema | Raise `MaxOutputTokens`; a repeat usually succeeds |
| "The data did not pass the privacy check" (5503) | The payload held a string that is not template text or a code, usually after a change to the payload builder | Read the JSON path in the 5503 entry; fix the builder, never the guard |
| The portal stops at start-up with an `Ai:` validation error | An invalid setting, such as an unknown effort or an `http` base URL | Fix the setting the message names |
| The button gives 403 | The user is not a supervision reviewer or approver | Expected for other roles |
| Refresh shows the same insight | Nothing changed since the last one (5502) | Expected; a new revision or approved prior period gives a new one |
| A stored insight no longer matches its audit entry | The row in `returns.ReturnInsights` was changed | Treat as tampering; the audit entry's digests are the reference |

## 13. Demo and reset

The demo pages, the reset and the administrator's directory ([guide](DEMO.md), ADR 0031) log:

```
EventId.Id = 5601                                  -- reset done (trigger, rows removed, obligations and returns seeded, ms, audit entry)
EventId.Id = 5602                                  -- reset refused (trigger, error code) (warning)
EventId.Id = 5603                                  -- next scheduled reset (expression, time)
EventId.Id = 5604                                  -- scheduled reset is off (demo mode or Demo:ResetSchedule not set)
EventId.Id = 5605                                  -- scheduled reset failed; nothing changed, tried again next time (error)
EventId.Id = 3011                                  -- a person's portal access was disabled or re-enabled
EventId.Id = 3012                                  -- change of portal access refused (error code) (warning)
```

Each reset runs in a `ResetDemo` activity and counts in `regreturns.demo.resets` (tags `trigger`, `outcome`,
`error_code`) and `regreturns.demo.reset.duration`. Every reset that ran is a `DemoReset` audit entry; filter the
audit trail on *Demo reset* to see them with what each removed and seeded.

| Symptom | Likely cause | Fix |
|---|---|---|
| `/demo` answers 404 and there is no banner | Demo mode is off (`Demo:Enabled` is `false` outside Development) | Set `Demo__Enabled=true` on the demo deployment only |
| `/demo` says the password is not configured, or shows no QR code for an account that needs one | `Demo:UserPassword` or `Demo:TotpSecrets:<USER>` is empty | Run `scripts/dev-secrets.sh` after IamBootstrap `apply`; on a server set `Demo__UserPassword` and `Demo__TotpSecrets__APPROVER_MFA` |
| The published TOTP key is refused at sign-in | WSO2's key changed (IamBootstrap `demo-users` enrols again) but the portal still has the old one | Copy the new `TOTP_SECRET_<USER>` values from `.env.generated` into the portal's settings and restart it |
| *Reset demo* says "reset a few minutes ago" | The cooldown (`Demo:ResetCooldownMinutes`) after the last reset, manual or scheduled | Wait until the time the message gives; the scheduled reset ignores the cooldown |
| *Reset demo* says "already running" (`Demo.InProgress`) | Another reset holds the `RegReturns.DemoReset` lock | Wait for it to finish; a reset takes seconds |
| *Reset demo* says the directory holds people who are not demo accounts (`Demo.NotADemoDatabase`) | The database has a real or test person in `iam.Users` | Expected: such a database is never reset. Use a dedicated demo database |
| No reset at night and 5604 at start-up | `Demo:ResetSchedule` is empty or demo mode is off | Set the schedule (default `0 3 * * *`, UTC) |
| The portal stops at start-up with a `Demo:` validation error | An invalid cron expression in `Demo:ResetSchedule`, or `Demo:ApiBaseUrl` not an absolute `https` URL | Fix the setting the message names |
| 5605 with an exception | The database was unavailable, or the seed could not be built | The transaction rolled back and the demo is unchanged; fix the cause, then use *Reset demo* |
| Disabling a person says demo accounts cannot be changed | Demo accounts are shared by every visitor | Expected |
| A disabled person still signs in to WSO2 | The portal only stops them acting in the portal (`User.NotLinked`) | Expected: the portal's switch covers the portal only. To stop the WSO2 sign-in as well, lock or disable the account in WSO2 (provisioning from the portal is in the [backlog](BACKLOG.md)) |
| `scripts/demo-scenario.sh` finds no MDA return for last month | The demo was not reset since the month changed, or someone already filed it | Reset the demo, then run the script again |

## 14. Security: headers, TLS, authenticator resets and diagnostics

Start at **Administration → Diagnostics** (`/admin/diagnostics`, system administrators with TOTP): every health check
with its duration and error, the build, the database's migrations and connection encryption, the audit chain head and
the settings that matter, with secrets shown only as *set* or *not set* (ADR 0033).

Authenticator resets ([ADR 0032](adr/0032-administrator-opened-totp-enrolment.md)) log:

```
EventId.Id = 3013                                  -- enrolment window opened (WSO2 user id, until, audit entry)
EventId.Id = 3014                                  -- reset refused (error code) (warning)
EventId.Id = 3015                                  -- WSO2 could not be used; nothing changed (error, with the cause)
```

| Symptom | Likely cause | Fix |
|---|---|---|
| A script, chart or style does not work and the browser console shows `Refused to execute inline script` or `Refused to apply inline style` | The page has inline code, which the Content Security Policy blocks | Move the script to `wwwroot/js` (loaded in the `Scripts` section) and the style to `site.css`; set styles from script with `element.style`. `PortalContentSecurityPolicyTests` names the view |
| A script from `wwwroot/js` is blocked | The `<script>` was written without the tag helper (for example in a string), so it lacks the nonce | Render it as a normal `<script src>` element in a view |
| `scripts/demo-scenario.sh` fails with `CSP violations` | A page of the tour broke the policy | The message names the page and the blocked resource |
| Swagger UI cannot get a token (`connect-src` violation) | `Wso2:Authority` of the API differs from the origin Swagger UI calls | Set the API's `Wso2:Authority` to WSO2's public origin |
| The app cannot connect: `The certificate chain was issued by an authority that is not trusted` or `does not match the certificate provided by the ServerCertificate option` | The connection string does not pin the current SQL Server certificate | Run `scripts/dev-secrets.sh` (it writes `ServerCertificate=.certs/sqlserver/mssql.crt`); after `dev-certs.sh --force`, run `docker compose up -d --force-recreate sqlserver-tls sqlserver wso2` |
| `sqlcmd` says `SSL Provider: certificate verify failed` or the login fails before authentication | SQL Server forces TLS and `sqlcmd` does not know the certificate | Add `-Ns -J .certs/sqlserver/mssql.crt` (inside the containers: `/var/opt/mssql/tls/mssql.crt`) |
| `sqlserver` never becomes healthy after an upgrade | The `sqlserver-tls` job did not run, so the certificate in `mssql.conf` is missing | `scripts/dev-certs.sh`, then `docker compose up -d` (the job runs before SQL Server starts); `docker compose logs sqlserver-tls` |
| WSO2 stops at start with a JDBC `trustAnchors parameter must be non-empty` or certificate error | The JDBC URL does not pin the certificate, or `.certs/sqlserver/mssql.crt` is not mounted | Keep `serverCertificate=` in `deploy/wso2/deployment.toml` and the mount in `docker-compose.yml` |
| Diagnostics shows *certificate NOT validated* | A connection string still has `TrustServerCertificate=True` | Replace it with `Encrypt=Strict;ServerCertificate=<path>` |
| *Reset an authenticator* says WSO2 could not be reached | The provisioner client is not configured (`Iam:Provisioner:*`), WSO2 is down, or the back channel is wrong | 3015 has the cause; run `scripts/dev-secrets.sh` after IamBootstrap `apply`; check `Wso2:BackchannelAuthority` |
| *Reset an authenticator* says the account holds no portal role | The account exists in WSO2 but has none of the portal's application roles | Expected for WSO2's own administrators; give the person a role through IamBootstrap first |
| The person is not asked to set up an authenticator | They signed in after the window closed, or they do not need TOTP (not an approver or administrator) | Open a new window; check the role |
| A person cannot sign in although the password is right | WSO2 locked the account after five failed sign-ins (it shows the same "login failed" message) | Wait `IamBootstrap:AccountLockMinutes` (5); SCIM shows `accountLocked` and `lockedReason` |
| `scripts/check-caddy.sh` fails | A change to `deploy/caddy/Caddyfile` | Each `FAIL` line names the request and what came back |

## 15. Deployment, backups and restore

Everything on the server goes through `deploy/regreturns.sh` ([DEPLOYMENT.md](DEPLOYMENT.md), ADR 0034). Start with
`deploy/regreturns.sh status` (deployed commit, containers and health, latest backup), then
`deploy/regreturns.sh compose logs --tail 100 <service>`. The apps log JSON with the trace id; Seq has the same events.
A failed `deploy` prints the command that rolls back; every step is idempotent, so fixing the cause and deploying the
same commit again is safe.

Migrator `verify-audit` logs:

```
EventId.Id = 2003                                  -- chain intact (entries checked, head sequence)
EventId.Id = 2004                                  -- chain broken: the first break (critical)
EventId.Id = 2005                                  -- verification could not run (error, with the cause)
```

| Symptom | Likely cause | Fix |
|---|---|---|
| `deploy` stops with `container ... is unhealthy` | The app cannot reach SQL Server or WSO2, or cannot read a mounted file | `compose logs --tail 100 web api`: the failing health check or start-up error is there |
| An app exits at start: `Wso2:TrustedCaPath must name a readable PEM file with at least one certificate` | `.certs/regreturns-dev-ca.crt` is missing or not readable by the app's user (uid 1654) | `scripts/dev-certs.sh` (it resets the folders to 0755 and the certificates to 0644), then deploy again |
| WSO2 stops at start: `cp: cannot access ... security: Permission denied` | `.certs/wso2` was created under a strict umask, so WSO2 (uid 802) cannot enter it | `scripts/dev-certs.sh`, then deploy again |
| `sqlservr` or Seq exits at once with `Operation not permitted` | The service lost `cap_add: [NET_BIND_SERVICE]`; its binary carries that file capability and Linux refuses to start it without one | Keep the `cap_add` line in `docker-compose.prod.yml` |
| Seq restarts with `Access to the path '/data/Seq.json' is denied` | Seq runs as root without capabilities, so it cannot write its own folder | Keep `user: seq` on the service |
| Signing in to the portal answers 500; the log says `An error occurred while reading the key ring` | The data-protection keys volume is not writable by the app (it was created by an older image) | `deploy/regreturns.sh compose rm -sf web`, `docker volume rm regreturns-prod_web-keys`, deploy again; people sign in again |
| Smoke: `signing in to the portal does not lead to WSO2`, or the browser gets `Forbidden` after the login form | Caddy refuses a WSO2 sign-in path (WSO2 7 also uses `/t/carbon.super/...`) | Check the `@signIn` paths in `deploy/caddy/Caddyfile`; `scripts/check-caddy.sh` |
| Smoke: `https://<host> is not answering over TLS` | DNS does not point at this server yet, ports 80 and 443 are closed, or Let's Encrypt refused | `dig +short <host>`, `ufw status`, `compose logs caddy` (it names the ACME error); run `deploy/regreturns.sh smoke` again later |
| Smoke: `the status page does not say every component is operational` | One dependency is down | `/status` names it; for the REST API row, `compose logs api` |
| `docker pull` says `denied` or `unauthorized` | The GHCR packages are private and this shell is not logged in | `docker login ghcr.io` with a token that can read packages, or make the packages public |
| The CI deploy says `refused: the deploy key only runs ...` or `... is not a commit on main` | The workflow's command changed, or the commit is not on `main` (only main deploys) | Deploy from a green run on `main`; the forced command is in `~regreturns/.ssh/authorized_keys` |
| The CI deploy says `Host key verification failed` | `DEPLOY_KNOWN_HOSTS` does not match the server (new server or new host key) | `ssh-keyscan -t ed25519 <server>`, compare the fingerprint with `ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub` on the server, update the secret |
| `backup` stops in `RESTORE VERIFYONLY` | The backup file is damaged as written (disk full or failing) | `df -h`; the unfinished folder is `backups/<stamp>.partial` and is removed after a day |
| `restore` says `backups/<stamp> is damaged: its checksums do not match` | A file changed after the backup or was copied incompletely | Use another backup, or copy that one again from the off-site archive |
| `restore` says `the restored audit chain does not verify; nothing was started` | `verify-audit` found a break (2004 names it) or `AUDIT_HMAC_KEY` in `.env` is not the key the chain was written with | [DR-RUNBOOK.md](DR-RUNBOOK.md#when-the-audit-chain-does-not-verify); check that `.env` came from the same archive |
| No backup for a day | The timer did not run or failed | `systemctl list-timers 'regreturns-*'`, `journalctl -u regreturns-backup` |

## 16. Guided tour, accessibility and documentation checks

`scripts/demo-scenario.sh` prints each step and, on failure, `FAIL` with the reason; the Release workflow keeps the
screenshots it took as an artifact (`screenshots-<sha>`), which show the last page each person saw (ADR 0035).

| Symptom | Likely cause | Fix |
|---|---|---|
| `FAIL the reset is cooling down: Available again from HH:mm UTC` | `--reset` within ten minutes of the last reset | Wait until the time shown, or run without `--reset` if nothing has changed since the reset |
| `no MDA return for <period> in the bank's list (reset the demo first)` | An earlier run already took that return to approval | Run with `--reset` |
| `the raised non-performing loans caused no warning to justify` | The seed or the MDA template changed so that a 50% rise in non-performing loans no longer trips a warning | Check the `MDA_NPL_MAX` and `MDA_NPL_VAR` rules and the seeded figures; the tour needs at least one warning |
| `accessibility violations (axe, WCAG 2.2 A and AA)` with a page, rule and element | A change broke a WCAG rule on that page: a missing label or caption, low contrast, a scrollable region without keyboard access | Fix the view; the rule id (such as `color-contrast`) links to its explanation at dequeuniversity.com/rules/axe |
| `reported (WSO2's page, not the portal's)` lines | WSO2's own sign-in pages break a rule | Informational: WSO2's branding and layouts can fix it (see the backlog) |
| `playwright not found` or `axe-core not found` | Node cannot find the packages | `npm install --global playwright@<version> axe-core@<version>` with the versions in `.github/workflows/release.yml`, or set `NODE_PATH` |
| CI job *Documentation* fails: `... does not exist` or `... has no heading for #...` | A link points at a moved file or a renamed heading | Fix the link; anchors are GitHub's: lower case, punctuation dropped, spaces as hyphens (`## 15. Deployment, backups and restore` is `#15-deployment-backups-and-restore`) |
| CI job *Documentation* fails: `docs/adr/README.md does not list ...` | A new ADR is not in the index | Add its row to `docs/adr/README.md` |
| A host stops at start in Development with `Some services are not able to be constructed ... Unable to resolve service for type ...` | Development validates every registration; a handler needs a service this host does not register | Register a fallback in `AddApplication` and `Replace` it in the Infrastructure method of the host that provides it (CLAUDE.md, Gotchas) |
