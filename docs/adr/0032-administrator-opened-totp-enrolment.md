# 32. Administrators open a TOTP enrolment window; the person enrols at their next sign-in

- Status: accepted
- Date: 2026-10-09

## Context

Approvers and administrators sign in with a password and a one-time code (plan §4.5). ADR 0020 switched off TOTP
enrolment during sign-in and My Account, so nobody can enrol or replace an authenticator by themselves: a stolen
password must never be enough to bind the thief's phone. Demo users are enrolled by IamBootstrap, which signs in as
each user with a known password (`DemoTotpStep`).

That leaves real people without a way in. A new approver has never enrolled, and an approver who loses their phone
cannot sign in again. WSO2 encrypts TOTP secrets with its own key, so an administrator cannot set a secret for
someone else, and the portal holds no WSO2 administrator credentials (ADR 0017, 0031).

## Decision

- **A per-user enrolment window in WSO2.** IamBootstrap creates a local claim `http://wso2.org/claims/totp_enrolment_until`
  (user store attribute `totpEnrolmentUntil`, read-only for users, never released in tokens) and maps it to the SCIM
  attribute `urn:scim:schemas:extension:custom:User:totpEnrolmentUntil`. It holds the end of the window in Unix
  milliseconds; `0` or empty means closed.
- **The sign-in script does the enrolment.** For a user who needs TOTP (approvers and administrators when MFA is
  enforced, and the always-MFA users), the adaptive script reads the claim after the password step. While the window
  is open it empties the claims in which WSO2 keeps the user's TOTP secret, runs the TOTP step with
  `enrolUserInAuthenticationFlow: 'true'` for this one execution, and on success writes `0` to the claim. Outside a
  window the TOTP step runs as before, and enrolment stays off for everyone else (ADR 0020).
- **The portal opens the window.** On `/admin/users` a system administrator (with TOTP, `AdminManage`) enters a user
  name and presses *Reset an authenticator*. `OpenTotpEnrolmentHandler` checks, in order, before WSO2 is asked
  anything: the caller is an administrator; the name is a valid WSO2 user name; it is not the migration account or an
  API client user; and if the person is in the portal directory, it is not the caller's own account, not a demo
  account, not an API client user and not disabled. It then finds the account in WSO2 by user name, refuses it if it
  is linked to a different WSO2 id (`User.IdentityConflict`) or holds no portal role (WSO2's own administrators are
  not the portal's to change), and sets the claim to now plus `Iam:TotpEnrolment:WindowHours` (72 by default, at most
  168). The person need not have signed in to the portal before, which is the new-approver case.
- **SCIM through the back channel with a narrow client.** The portal calls WSO2's SCIM 2 API with the provisioner
  client IamBootstrap already creates (ADR 0020), using the client credentials grant and only
  `internal_user_mgt_list internal_user_mgt_view internal_user_mgt_update`. Its credentials are user-secrets or
  environment variables (`Iam:Provisioner:ClientId`, `Iam:Provisioner:ClientSecret`; `scripts/dev-secrets.sh` copies
  them from `.env.generated`). Calls go server to server through the WSO2 back channel (`Wso2:BackchannelAuthority`),
  never through the public edge, which refuses `/scim2` (ADR 0033). The token is cached until a minute before it
  expires; every call has a deadline (`Iam:Provisioner:TimeoutSeconds`). A WSO2 failure is reported as
  `Identity.DirectoryUnavailable` with nothing changed, and its log names the method, path and status, never the body.
- **Audited.** Every window opened is a `TotpEnrolmentOpened` audit event (new `AuditAction` 13) with the WSO2 id as
  the entity, and the user name and end of the window as details, recorded after WSO2 accepted the change. Log events
  3013 (opened, with the WSO2 id), 3014 (refused, with the error code) and 3015 (WSO2 failure). Refusals are not
  audited: nothing changed.

## Consequences

- An administrator can bring in a new approver or recover one who lost their phone, without WSO2 Console access and
  without ever seeing a TOTP secret. Every reset is in the audit chain with who did it.
- **While a window is open, the password alone is enough to enrol.** Whoever signs in first with the password binds
  their authenticator. The window is short and configurable, opened for one named person, refused for one's own
  account, and audited; the administrator should tell the person through a channel other than e-mail to sign in
  promptly. A person who finds their authenticator rejected after a reset they did not expect reports it, and the
  audit entry shows who opened the window.
- The old authenticator keeps working until the person signs in inside the window; the secret is removed only at
  that sign-in. If the window closes unused, nothing has changed.
- A user who does not need TOTP is unaffected by an open window (the script returns after the password step).
- The portal now holds a WSO2 client secret that can update user attributes. Its scopes cannot change passwords,
  roles or applications, and WSO2 rejects the token for anything else. A later SCIM user-management feature should
  reuse this client and its narrow scopes rather than add an administrator credential.
- Live check on 2026-10-09: a reset user signed in, was asked to scan a new QR code, enrolled, and the claim returned
  to `0`; the portal page sent no CSP violations.
