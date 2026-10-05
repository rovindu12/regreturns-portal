# 19. Portal sign-in and session lifetime

- Status: accepted
- Date: 2026-10-04

## Context

The portal signs users in through WSO2, the only identity store (no ASP.NET Core Identity tables). Regulators expect short idle sessions, a hard
upper bound on any session, sign-out that really ends the session at the identity provider, and a clear trail of who
signed in, out or was refused.

## Decision

- **OpenID Connect authorization code flow with PKCE (S256)** and `response_mode=form_post`, the flow verified against
  WSO2 7.3. Pushed authorization requests are off by default (`Oidc:UsePushedAuthorization`): WSO2 advertises PAR and
  ASP.NET Core would use it automatically, but the plain code flow is the tested path. Scopes are
  `openid profile email roles institution`; never `internal_login`, which would unlock WSO2's self-service APIs.
- **Tokens are not kept.** `SaveTokens=false`; only the ID token is stored in the cookie ticket, for `id_token_hint` at
  sign-out. The portal does not call the API on the user's behalf.
- **Claims are normalised once** at sign-in (`PortalClaims`): `roles` (string or array), `institution_id`, the display
  name (`name`, falling back to given and family name, then user name). The WSO2 subject is linked just in time to the
  `AppUser` row (`LinkSignedInUser`); a token whose roles or institution make no sense for RegReturns is refused with a
  stable error code and an audit entry.
- **Session lifetime:** a `__Host-` cookie with a 20-minute sliding expiry and an 8-hour absolute limit. The absolute
  expiry is stamped into the ticket at sign-in and checked on every request; a ticket without it counts as expired.
- **Sign-out** clears the cookie and redirects to WSO2's end-session endpoint with `id_token_hint`, which ends the WSO2
  session too. **Back-channel logout** (`/signout-backchannel`) validates the logout token (signature against WSO2's keys,
  issuer, audience, the back-channel event, a `sid`, no `nonce`, issued within 5 minutes) and puts the session id on a
  deny list in `IDistributedCache` (SHA-256 of the `sid`, kept for 8 hours 10 minutes). Cookies carrying a denied `sid`
  are rejected.
- **Failures** go to `/Account/SignInFailed` with a short allow-listed code and the trace id, never WSO2's raw message.
  SignIn, SignOut, AccessDenied and AuthenticationFailed are written to the audit trail (ADR 0016), de-duplicated per
  minute so refusals cannot flood it.

## Consequences

- The deny list is in memory, so it is per instance. Running more than one portal instance needs a shared
  `IDistributedCache` (SQL Server or Redis); the code does not change.
- Locally WSO2 runs in Docker and cannot reach or trust the portal's development certificate, so back-channel logout is
  covered by tests rather than by the local stack; RP-initiated sign-out works everywhere.
- Hosted tests that render signed-in pages must use an `https://localhost` base address, because the antiforgery and
  session cookies are `Secure`.
