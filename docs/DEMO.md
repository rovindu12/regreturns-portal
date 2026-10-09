# The public demo

The demo lets anyone explore RegReturns as each of its people: a bank's maker and checker, the Bank of Valoria's
reviewer, approver, administrator and auditor. Every bank, person and figure in it is fictional. The decisions behind
it are in [ADR 0031](adr/0031-public-demo-and-demo-reset.md).

## Pages

| Page | Who | What it shows |
|---|---|---|
| `/` | Everyone | What the portal does, the architecture diagram and who does what. In demo mode: *Try the demo* and the guided tour |
| `/demo` | Everyone, demo mode only | Every active demo account by role, with a *Sign in as* button, the shared password, the authenticator key and QR code of each account that signs in with a one-time code, and the read-only API client |
| `/demo/guide` | Everyone, demo mode only | The guided tour: one return from draft to approval in six steps |
| `/status` | Everyone | The portal, the database and WSO2 as operational, degraded or down; in demo mode the last and next reset |
| `/admin` | System administrator | The *Reset demo* button and the latest resets |
| `/admin/users` | System administrator | People and API clients; disable or re-enable a person's portal access |

In demo mode a yellow banner on every page says it is a demo and when the next reset is. Outside demo mode (the
default) `/demo` and `/demo/guide` answer 404, there is no banner, and nothing points at them.

*Sign in as* passes the user name to WSO2 as `login_hint`, so the sign-in page starts with it; the visitor types the
password (and the one-time code) as any user would.

## Accounts

All demo accounts share one password (`DEMO_USER_PASSWORD`). Accounts that need a second factor are pre-enrolled for
TOTP by IamBootstrap, and the demo page publishes their keys.

| User name | Role | Bank | One-time code |
|---|---|---|---|
| `maker.hlb`, `maker.ccb`, `maker.lub`, `maker.nsb`, `maker.mdb` | Bank maker | Their bank | No |
| `checker.hlb`, `checker.ccb`, `checker.lub`, `checker.nsb`, `checker.mdb` | Bank checker | Their bank | No |
| `reviewer` | Supervision reviewer | Bank of Valoria | No |
| `approver` | Supervision approver | Bank of Valoria | Yes, while `EnforceMfa` is on |
| `approver.mfa` | Supervision approver | Bank of Valoria | Always |
| `admin.demo` | System administrator | Bank of Valoria | Yes, while `EnforceMfa` is on |
| `auditor` | Auditor | Bank of Valoria | No |

The banks are Harbourline Bank PLC (HLB), Crestmont Commercial Bank (CCB), Lotus Union Bank (LUB), Northgate Savings
Bank (NSB) and Meridian Development Bank (MDB). Demo accounts cannot change their password or two-step settings
(WSO2 self-service is closed, ADR 0020), and the administrator cannot disable them.

The **API demo client** (`DEMO_API_CLIENT_ID`) is read-only (`reference:read returns:read`) and acts for
Harbourline. Its secret is published so visitors can use *Authorize* in Swagger UI.

## The story in the data

Every reset seeds twelve months of returns ending with the last completed month, so the story always looks recent:

- **Harbourline** (HLB): non-performing loans jumped two months ago (NPL ratio warning, justified by the bank) and
  are recovering; last month's MDA is a draft. This is the return the guided tour takes through the workflow.
- **Lotus Union** (LUB): a large corporate withdrawal three months ago pushed the LCR below 100%; last month's
  liquidity return is waiting for review.
- **Crestmont** (CCB): its liquidity return six months ago was sent back for correction (Level 2B assets above the
  cap) and approved as revision 2; last month's MLR is under review.
- **Meridian** (MDB): the MDA five months ago was never filed and shows as overdue.
- **Northgate** (NSB): two quarterly capital returns were filed late.

The [reports](../README.md#reports) show all of this; the reviewer can generate an advisory insight on any submitted
return.

## Guided tour

The tour on `/demo/guide` takes Harbourline's MDA from draft to approval: maker.hlb validates it, checker.hlb submits
it, the reviewer starts the review and generates an insight, approver.mfa approves it after the TOTP step, and the
auditor verifies the audit chain. A browser script plays the same tour against a running demo:

```bash
scripts/demo-scenario.sh                         # local: https://localhost:7101 and WSO2 on 9443
PORTAL_BASE=https://demo.example APP_CA=system WSO2_BASE=https://iam.demo.example scripts/demo-scenario.sh
```

It reads the password and the authenticator key from `/demo`, as a visitor would, and needs Node with Playwright. It
changes data, so run it after a reset; the next reset undoes it.

## Reset

The demo resets itself every night at 03:00 UTC (`Demo:ResetSchedule`), and the administrator can reset it from
`/admin`, at most once every ten minutes (`Demo:ResetCooldownMinutes`). A reset, in one transaction:

1. takes the `RegReturns.DemoReset` application lock without waiting (a second reset at the same time is refused);
2. checks the cooldown (button only) and that every person in the directory is a demo account; a database with
   anyone else in it is never reset (`Demo.NotADemoDatabase`);
3. deletes returns, values, findings, workflow history, uploads, insights, obligations, templates, migration runs and
   idempotency records;
4. seeds them again for today, for the existing banks and demo accounts (creating any that are missing);
5. appends one `DemoReset` entry to the audit trail, saying what was removed and seeded.

Banks, people, API clients and the **audit trail are kept**: the chain keeps growing across resets and still
verifies, and the auditor can filter on *Demo reset*. Sessions stay valid.

The portal never holds WSO2 administrator credentials, so the WSO2 side of the reset (passwords, roles and TOTP keys
of the demo users) is a separate job on the host, scheduled just before the portal's reset:

```bash
dotnet run --project tools/RegReturns.IamBootstrap -- demo-users
```

## Settings

`Demo` section of the portal's configuration (environment variables use `__`, such as `Demo__Enabled`):

| Setting | Default | Meaning |
|---|---|---|
| `Enabled` | `false` (`true` in Development) | This deployment is the public demo |
| `UserPassword` | none (secret) | The shared password, published on `/demo` |
| `TotpSecrets:<USER>` | none (secret) | An account's TOTP key, published on `/demo`; `<USER>` is the user name in upper case with `_` for other characters (`APPROVER_MFA`), as IamBootstrap writes `TOTP_SECRET_<USER>` |
| `ApiClientId`, `ApiClientSecret` | none (secret) | The read-only API client published on `/demo` |
| `ApiBaseUrl` | none (`https://localhost:7201/` in Development) | Where the API is, for the Swagger links |
| `ResetSchedule` | `0 3 * * *` | Five-field cron expression in UTC; empty turns the nightly reset off |
| `ResetCooldownMinutes` | `10` | How long after any reset the button is refused |

Locally, `scripts/dev-secrets.sh` copies the password, the TOTP keys and the API client from `.env` and
`.env.generated` into the portal's user-secrets. Problems with the reset are in
[troubleshooting §13](TROUBLESHOOTING.md#13-demo-and-reset).
