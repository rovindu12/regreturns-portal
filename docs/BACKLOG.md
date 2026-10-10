# Product backlog

The work behind RegReturns as user stories, grouped into the twelve sprints the build ran in (one sprint per phase of
the [implementation plan](IMPLEMENTATION-PLAN.md)), followed by the backlog that is left. Each story names who wants
it, what they want and why; the acceptance criteria are the tests and documents that prove it. Decisions taken along
the way are in the [ADRs](adr/README.md) and every change is in the [changelog](../CHANGELOG.md).

Status: sprints 1 to 12 are done. The hosted demo goes live once a domain and a server are provided
([DEPLOYMENT.md](DEPLOYMENT.md)).

## Personas

| Persona | Organisation | Wants |
|---|---|---|
| Maker (Nimal, Harbourline Bank) | Bank | To prepare a return quickly, by hand or from the bank's spreadsheet, and see every problem before anyone else does |
| Checker (Ayesha, Harbourline Bank) | Bank | To submit only returns she has checked, explaining every warning, and never one she prepared herself |
| Reviewer (Supervision department) | Bank of Valoria | A worklist of submitted returns and a quick read of what changed and why it matters |
| Approver (Supervision department) | Bank of Valoria | To decide on a return with confidence that someone else reviewed it, behind a second factor |
| Auditor (Internal audit) | Bank of Valoria | Proof that nobody changed the record after the fact |
| System administrator (IT) | Bank of Valoria | Templates and rules without a release, access under control, a system that explains itself when it fails |
| Bank system (Harbourline's core banking) | Bank | To deliver a return without a person retyping it, safely retried |

The people are fictional, as are the banks and the regulator.

## Sprint 1: foundations

- As the **development team**, we want a layered solution with enforced dependencies, zero-warning builds and CI, so
  that the codebase stays maintainable as it grows.
  *Accepted:* architecture tests, `TreatWarningsAsErrors`, central package management with lock files, CI on every push
  ([ADR 0002](adr/0002-clean-architecture-with-service-defaults.md), [ADR 0011](adr/0011-testing-platform-and-coverage.md)).
- As a **system administrator**, I want every request to carry one trace id through logs, traces and error pages, so
  that I can follow a failure from a user's screenshot to its cause.
  *Accepted:* Serilog and OpenTelemetry into Seq, `X-Trace-Id`, redaction enricher ([ADR 0010](adr/0010-observability-serilog-opentelemetry-seq.md)).
- As a **reviewer**, I want realistic demo data with known anomalies, so that the dashboards and rules have something
  to find. *Accepted:* five banks, three return types, twelve months of history seeded through the domain
  ([ADR 0012](adr/0012-seed-data-through-the-domain.md)).
- As an **auditor**, I want the workflow states and segregation-of-duties rules in one place, so that no screen can
  bypass them. *Accepted:* `SubmissionWorkflow` and `Submission.Permits` with unit tests for every transition.

## Sprint 2: identity

- As a **user**, I want to sign in once through the regulator's identity provider, so that the portal never holds my
  password. *Accepted:* OIDC code flow with PKCE against WSO2, back-channel logout ([ADR 0019](adr/0019-portal-sign-in-and-sessions.md)).
- As a **system administrator**, I want WSO2 configured by code, so that a new environment is rebuilt in minutes, the
  same way every time. *Accepted:* IamBootstrap `apply` is idempotent ([ADR 0017](adr/0017-iam-bootstrap.md)).
- As an **approver**, I want approvals to need a one-time code, so that a stolen password is not enough to approve a
  return. *Accepted:* TOTP in WSO2's adaptive script, `amr` checked by the approval policy ([ADR 0020](adr/0020-demo-mfa-and-self-service-lockdown.md)).
- As a **bank system**, I want a client-credentials token scoped to my bank, so that I can call the API without a
  person. *Accepted:* JWT validation and institution scoping ([ADR 0018](adr/0018-api-token-validation-and-institution-scoping.md)).

## Sprint 3: templates and validation

- As a **system administrator**, I want to version return templates and their rules, so that a change applies from a
  given period without touching returns already filed. *Accepted:* draft, publish and retire versions; captured
  version by period start ([ADR 0009](adr/0009-versioned-templates.md)).
- As a **maker**, I want to enter figures or upload the bank's Excel or CSV file, so that I do not retype the
  spreadsheet. *Accepted:* uploads checked by content and size and never served ([ADR 0022](adr/0022-safe-return-file-uploads.md)).
- As a **maker**, I want every problem listed next to its field, errors and warnings apart, so that I can fix the
  return before submitting it. *Accepted:* `ValidationEngine` with required, type, range, cross-field and variance rules
  ([ADR 0021](adr/0021-in-house-rule-expressions.md)).
- As a **checker**, I want to justify each warning, so that the regulator sees why an unusual figure is right.

## Sprint 4: workflow and audit trail

- As a **checker**, I want to submit only a return I did not prepare or last edit, so that two people see every
  return. *Accepted:* `Submission.CheckerIsMaker` refusal, tested at domain and page level.
- As a **reviewer** and **approver**, I want a worklist and the steps open to me on each return, so that I never
  press a button that will be refused ([ADR 0025](adr/0025-workflow-steps-and-supervision-visibility.md)).
- As an **auditor**, I want a tamper-evident trail of every change and decision, so that I can prove the record is
  complete. *Accepted:* HMAC hash chain, append-only trigger, verify screen, tamper tests for edited, deleted and
  reordered rows ([ADR 0016](adr/0016-hash-chained-audit-trail.md), [ADR 0024](adr/0024-auditing-data-changes.md)).
- As a **maker**, I want to be told when someone else saved the return after I opened it, so that I never overwrite
  their work ([ADR 0023](adr/0023-one-live-return-per-obligation.md)).

## Sprint 5: Web API

- As a **bank system**, I want to deliver a whole return as a draft and get the validation findings back, so that the
  bank's checker can submit it in the portal ([ADR 0026](adr/0026-api-delivers-drafts-through-a-client-user.md)).
- As a **bank system**, I want idempotency keys, so that a retried delivery never creates a second return.
- As the **regulator**, we want rate limits, paging and consistent problem answers with codes, so that clients behave
  well and failures are diagnosable ([ADR 0027](adr/0027-web-api-v1-conventions.md), [API.md](API.md)).

## Sprint 6: dashboards and reports

- As a **reviewer**, I want a bank by period grid of who filed on time, late or not at all, so that I chase the right
  banks. *Accepted:* `Compliance.StateOf`, reporting views, scoped queries ([ADR 0028](adr/0028-reporting-views-and-exports.md)).
- As a **reviewer**, I want trends of validation findings and key ratios (LCR, NPL ratio, capital adequacy), so that I
  see a bank drifting before it breaches.
- As a **supervision manager**, I want the compliance report as Excel or PDF, with every download audited.

## Sprint 7: legacy migration

- As the **regulator**, we want filed returns moved from the old system with every row checked and the totals
  reconciled, so that history is complete on day one. *Accepted:* dry run, mapping, error report, reconciliation,
  commit only when reconciled ([ADR 0029](adr/0029-legacy-data-migration.md), [DATA-MIGRATION.md](DATA-MIGRATION.md)).

## Sprint 8: advisory insights

- As a **reviewer**, I want an advisory note on a return (largest movements, failed rules, questions for the bank),
  so that I start the review knowing where to look. *Accepted:* figures-only payload with a guard, computed facts,
  Claude or a rule-based fallback, every call audited with digests ([ADR 0030](adr/0030-advisory-return-insights.md),
  [AI-ASSISTANT.md](AI-ASSISTANT.md)).

## Sprint 9: public demo

- As a **visitor**, I want to try every role from a public page and follow a guided tour, so that I understand the
  product in ten minutes. *Accepted:* `/demo`, `/demo/guide`, `/status`, nightly reset that keeps the audit chain
  ([ADR 0031](adr/0031-public-demo-and-demo-reset.md), [DEMO.md](DEMO.md)).
- As a **system administrator**, I want to disable or re-enable a person's access, sandboxed so that demo accounts
  stay usable for the next visitor.

## Sprint 10: security hardening

- As the **regulator's security team**, we want a strict Content Security Policy, anti-forgery on every form, forced
  and pinned TLS to SQL Server, account lock after failed sign-ins and a written threat model, so that the portal
  meets the bank's security standards ([ADR 0033](adr/0033-security-hardening.md), [SECURITY.md](SECURITY.md)).
- As a **system administrator**, I want to let a person enrol a new authenticator without ever seeing their secret
  ([ADR 0032](adr/0032-administrator-opened-totp-enrolment.md)).
- As a **system administrator**, I want a diagnostics page that shows configuration and dependencies without secrets.

## Sprint 11: containers and release

- As the **operations team**, we want non-root images, scanned and with SBOMs, deployed by a pipeline that proves the
  whole stack in a browser before release, so that a release is boring ([ADR 0034](adr/0034-containers-release-pipeline-and-hosting.md)).
- As the **operations team**, we want verified backups and a rehearsed restore, so that a lost server costs hours, not
  data ([DEPLOYMENT.md](DEPLOYMENT.md), [DR-RUNBOOK.md](DR-RUNBOOK.md)).

## Sprint 12: documentation and polish

- As a **new developer**, I want architecture, identity and API guides with diagrams, so that I am productive without
  a walkthrough ([ARCHITECTURE.md](ARCHITECTURE.md), [IAM.md](IAM.md), [API.md](API.md)).
- As a **user**, I want a guide for my role with screenshots ([USER-GUIDE.md](USER-GUIDE.md)).
- As a **user with a disability**, I want every page to meet WCAG 2.2 AA, so that I can do my job with a keyboard or a
  screen reader. *Accepted:* axe on every page of the browser tour in the release pipeline, a proper "page not found"
  page, keyboard-scrollable tables ([ADR 0035](adr/0035-documentation-and-accessibility-checks.md)).
- As a **maintainer**, I want broken links in the documentation to fail CI, so that the docs stay trustworthy.

## Future backlog

Ordered by value to a real deployment. None of these is needed for the demo.

| # | Story | Notes |
|---|---|---|
| 1 | As the **regulator**, we want obligations generated from each bank's licence and the return calendar, so that nobody creates them by hand | Today obligations come from the seed and the legacy migration; a scheduled job (Cronos, like the demo reset) would add the next period's |
| 2 | As a **system administrator**, I want to invite and provision real users from the portal, so that joiners and leavers are handled in one place | Through SCIM with an outbox for retries, reusing the provisioner client with the create and role scopes added. Today IamBootstrap creates the demo accounts, other accounts are made in WSO2, and the portal can only disable access |
| 3 | As a **checker**, I want an e-mail when a return is returned for correction or approved | Needs a mail relay and templates; never with figures in the message |
| 4 | As the **operations team**, we want transaction log backups every hour, so that the recovery point is an hour, not a day | SQL Server Express allows it; the runbook describes the change |
| 5 | As the **security team**, we want uploads scanned for malware before they are read | ClamAV as a sidecar behind an `IFileScanner`; uploads are already never served or executed |
| 6 | As a **visually impaired user**, I want WSO2's sign-in pages to meet WCAG 2.2 AA as the portal does | The tour reports WSO2's own findings (an invalid `lang`, link contrast); WSO2's branding and layout customisation can fix them |
| 7 | As a **bank system**, I want to submit in XBRL, so that the bank reuses its existing reporting toolchain | A second parser behind the same `FieldValueParser` and validation |
| 8 | As a **reviewer**, I want to compare a bank with its peer group on the dashboards | Needs peer groups on institutions; the reporting views already aggregate per bank |
| 9 | As the **regulator**, we want a high-availability database, so that maintenance does not stop filing | Beyond one server: SQL Server Standard with an availability group, or a managed database |
| 10 | As a **user**, I want the portal in Sinhala and Tamil as well as English | Resource files for views and validation messages; templates already hold their own field labels |
