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
