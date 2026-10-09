# 31. Public demo pages, and a demo reset that replaces the workload but keeps the directory and the audit chain

- Status: accepted
- Date: 2026-10-09

## Context

Phase 9 turns the portal into something a visitor can try without help (plan §4.6, §10): a landing page that explains
the system, a page with the demo accounts and the published Swagger client, a guided scenario, a status page, a banner,
and a nightly reset with an administrator's *Reset demo* button limited to one press per ten minutes. Visitors share
the demo accounts, so anything one of them changes (a template, a return, a decision) must be undone without anyone's
help, and nothing a visitor does may break the demo for the next one.

The plan said the reset starts "a fresh audit chain with a `DemoReset` genesis entry" and runs IamBootstrap's
`demo-users` step. Since then the audit table became append-only at the database (an `INSTEAD OF UPDATE, DELETE`
trigger, ADR 0024), and the portal holds no WSO2 administrator credentials: only IamBootstrap does (ADR 0017). Both
make the planned reset impossible as written without weakening a guarantee.

## Decision

- **Demo mode is a setting, off by default.** `Demo:Enabled` is `false` in `appsettings.json` and `true` in
  `appsettings.Development.json` and in the hosted demo's environment. Only in demo mode does the portal show the
  banner, the demo pages, the reset button and the published credentials, and run the reset job. A deployment with
  real data never publishes a password by accident and can never be reset.
- **Public pages are anonymous and read-only.** `/` (landing), `/demo` (accounts), `/demo/guide` (scenario) and
  `/status` carry `[AllowAnonymous]` and only `GET` actions; the endpoint metadata test lists them. `/demo` and
  `/demo/guide` answer 404 outside demo mode.
- **Published credentials come from configuration, never from WSO2 or the database.** `Demo:UserPassword`,
  `Demo:TotpSecrets:<USER>` (keyed like IamBootstrap's `TOTP_SECRET_<USER>` settings, one shared normalisation in
  `DemoAccounts.SettingSuffix`) and `Demo:ApiClientSecret` are filled from `.env` and `.env.generated` by
  `scripts/dev-secrets.sh`, or from environment variables on the server. The account list itself is read from the
  directory (`IsDemoAccount` users), so the page shows exactly the accounts that exist. A TOTP secret is shown as text
  and as a QR code (`otpauth://totp/...`, rendered to SVG on the server by QRCoder), only if it is Base32.
- **The reset replaces the workload and keeps the directory and the audit chain.**
  - Kept: institutions, users, API clients and every audit entry. Links to WSO2 users and signed-in sessions survive.
  - Replaced: everything else (insights, stored files, returns with their values, findings and history, obligations,
    legacy migration runs, templates and return types, idempotency records), deleted in foreign-key order and seeded
    again by the same `DemoDataBuilder` as `migrate-db --seed`, given the existing institutions and users.
  - `DemoResetService.WorkloadTables` and `KeptTables` together must name every table in the EF model; a unit test
    fails when a new table is in neither, so a later phase cannot forget to decide.
  - **One transaction**, inside the execution strategy, on a context without the data-change auditor (seeding writes
    no entries, as in the migrator). It takes an application lock (`RegReturns.DemoReset`, no wait: a second reset
    while one runs is refused as *in progress*), checks the cooldown, deletes, seeds, and appends one `DemoReset`
    audit event under the chain lock (ADR 0016) before it commits. Either all of it happens or none of it does, and
    two presses cannot both pass the cooldown.
  - The `DemoReset` event (new `AuditAction` 12) names the trigger (scheduled or by whom), the rows removed and
    seeded per area, and the duration. The chain is never truncated: the event marks the boundary instead. This
    replaces the plan's "fresh chain", which would need the append-only trigger disabled and would let anyone who can
    press the button erase the evidence of what visitors did.
- **Safety interlocks.** The reset refuses unless demo mode is on, and unless every person in the directory is a demo
  account (API client users and the migration account aside), so a database with real users cannot be wiped even if
  the setting is switched on by mistake. Only a system administrator may press the button; the scheduled run acts as
  the system actor, which no request can claim.
- **Cooldown from the audit trail.** The button is refused for `Demo:ResetCooldownMinutes` (10) after the latest
  `DemoReset` entry, scheduled or manual; the scheduled run ignores it. No extra table or in-memory state, so the rule
  holds across restarts and several instances.
- **Scheduling with a cron expression.** A hosted `DemoResetJob` computes the next occurrence of
  `Demo:ResetSchedule` (default `0 3 * * *`, UTC) with Cronos and runs the reset then. An empty schedule switches the
  job off (integration tests do this). A failed run is logged and tried at the next occurrence; it never stops the host.
- **WSO2 is reset by IamBootstrap, not by the portal.** Visitors cannot change WSO2 data: self-service is closed, demo
  passwords and MFA cannot be changed (ADR 0020) and the portal has no user-management screens that write to WSO2. The
  host scheduler runs `iam-bootstrap demo-users` nightly as well (wired in phase 11), which restores passwords, roles
  and TOTP enrolment. The portal keeps no WSO2 administrator secret.
- **Sandboxed administration.** The administrator's directory page lists people and API clients and can disable or
  re-enable a person's portal access (sign-in is refused while disabled). Demo accounts, system and client accounts,
  and the administrator's own account are refused (`User.DemoAccountProtected`, `User.SystemAccountProtected`,
  `User.CannotChangeOwnAccess`); the page offers no action on them. Template changes are allowed and undone by the
  next reset.
- **The status page reads the readiness checks.** `/status` runs the `ready`-tagged health checks (database, WSO2)
  at most every 15 seconds (cached), and shows each as operational, degraded or down, with the version and, in demo
  mode, the last and next reset. It never shows check descriptions or exceptions; `/health/ready` stays the endpoint for
  machines.
- **The architecture diagram is Mermaid rendered ahead of time.** `docs/diagrams/architecture.mmd` is the source;
  `scripts/render-diagrams.sh` renders it with a pinned Mermaid CLI to `wwwroot/img/architecture.svg` (plain SVG text,
  no HTML labels), which is committed and shown by the landing page as an image. The build needs no Node; a unit test
  checks the file is plain SVG without scripts or `foreignObject`. No Mermaid JavaScript runs in the portal, which keeps
  the phase 10 content security policy strict.
- **"Sign in as" passes a login hint.** Each card on `/demo` signs in with `login_hint=<user name>`; WSO2 then shows the
  chosen account and asks only for the password (and the one-time code), so the visitor still types the credentials
  as any user would. The portal never signs anyone in on their behalf.
- **The seed must build on any reset date.** The story is anchored on the last completed month, so the planted
  warnings, justifications and late filings move with the calendar. A unit test seeds for the first of every month
  over ten years and fails if any return trips a rule the story does not plan for. `scripts/demo-scenario.sh` plays
  the guided tour in a browser against a running demo (WSO2 sign-in with TOTP included) as the end-to-end check.

## Consequences

- A visitor can explore every role from `/demo`, and the next visitor finds the same story after the nightly reset.
- The audit trail grows across resets; entries before a `DemoReset` refer to rows that no longer exist. The auditor's
  filter shows the reset entries, and chain verification still covers everything.
- Users created outside the seed (none can be today) would survive a reset. When user management through SCIM arrives,
  the reset must also delete visitor-created users, and IamBootstrap's `demo-users` step their WSO2 accounts.
- Legacy migration results are part of the workload and are removed; run the migrator's `legacy` verb again to load
  them.
- A reset holds table locks for a few seconds. Requests that write during it wait or retry (deadlock victims are
  transient errors under the execution strategy).
