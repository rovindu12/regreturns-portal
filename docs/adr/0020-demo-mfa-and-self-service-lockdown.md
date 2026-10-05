# 20. Demo TOTP enrolment and self-service lockdown

- Status: accepted
- Date: 2026-10-04

## Context

Approvals need a second factor (TOTP), and the hosted demo must let visitors try it, so one demo approver
(`approver.mfa`) needs a TOTP secret the demo can publish. Demo users must not be able to change their password or MFA.
The plan's preferred route was to set a known secret over SCIM, with enrolling as the user as the fallback, decided by a
spike at the start of phase 2.

## Decision

- **Spike outcome: the fallback.** WSO2 7.3 stores the TOTP secret encrypted with its own internal key; a plaintext
  secret written over SCIM is stored but fails at sign-in with a decryption error. There is no TOTP admin service in 7.3.
- **IamBootstrap enrols as the user** (`demo-totp` step): a temporary password-grant app gets the user an
  `internal_login` token, `POST /api/users/v1/me/totp` with `INIT` makes WSO2 generate the secret (returned inside the
  `otpauth://` URI), one computed code sent with `VALIDATE` activates it, and the temporary app is deleted. Without the
  `VALIDATE`, WSO2 sends the user to its enrolment page at sign-in.
- The secret goes to the generated secrets file (`TOTP_SECRET_APPROVER_MFA`, mode 600). A known secret that is still
  active is kept on later runs and demo resets, so visitors do not have to re-scan the QR code after every reset.
  Phase 9 shows it on the demo page and stores it with Data Protection if it moves into the database.
- **Who gets the second factor:** the portal app's adaptive script runs the TOTP step for users holding
  `supervisor_approver` or `portal_admin` (when `EnforceMfa` is on) and always for `approver.mfa`. The portal checks
  `amr` contains `totp` for approvals (`MfaRequirement`), so the rule holds even if the script is edited in WSO2.
- **Self-service lockdown** (`self-service` step, on by default): self-registration, lite registration and password and
  user-name recovery are pinned off, and WSO2's My Account app is disabled (only `applicationEnabled` is patched, so its
  roles survive). The portal never requests `internal_login`, so its tokens get 403 from `/scim2/Me` and
  `/api/users/v1/me/*`. The smoke script checks both.

## Consequences

- Each run that has to enrol creates and deletes a password-grant app for a few seconds. An interrupted run leaves it
  behind; the next run reuses and deletes it.
- Turning the lockdown off (`IamBootstrap:LockDownSelfService=false`) is for installations with real users who should
  manage their own accounts.
- The TOTP code check depends on this machine's clock and WSO2's agreeing within 30 seconds.
