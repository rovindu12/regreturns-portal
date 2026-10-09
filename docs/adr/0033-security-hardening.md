# 33. Security hardening: browser policies, account lock, strict TLS to SQL Server, a deny-by-default edge and diagnostics

- Status: accepted
- Date: 2026-10-09

## Context

Phase 10 (plan §10) hardens what phases 1 to 9 built before anything is deployed: the headers browsers act on, the
forms they post, the connection to SQL Server (still `TrustServerCertificate=True`, ADR 0014), the edge that will
face the internet, the logs, and the supply chain. It also adds a diagnostics page, so an administrator can see why
something fails without shell access to the server. The threat model these choices answer is in
[docs/SECURITY.md](../SECURITY.md).

## Decision

### Browser policies

- **One middleware, two policies.** `UseSecurityHeaders` (ServiceDefaults) sets the headers in `OnStarting`, so they
  are on every answer: pages, errors, 404s, static files and health checks. It sends `X-Content-Type-Options: nosniff`,
  `X-Frame-Options: DENY`, a `Referrer-Policy`, a `Permissions-Policy` that denies camera, microphone, geolocation,
  payment and the like, `Cross-Origin-Opener-Policy: same-origin` and `Cross-Origin-Resource-Policy: same-origin`,
  and removes `Server` and `X-Powered-By` (Kestrel's `Server` header is switched off as well).
- **The portal's Content Security Policy uses a nonce per response.**
  `default-src 'self'; script-src 'nonce-…'; style-src 'self'; img-src 'self' data:; font-src 'self';
  connect-src 'self'; object-src 'none'; base-uri 'none'; form-action 'self' <WSO2 origin>; frame-ancestors 'none';
  upgrade-insecure-requests`. The nonce is 128 random bits in URL-safe Base64 (which HTML encoding leaves alone), created
  on first use per request. A tag helper
  (`CspNonceTagHelper`) puts it on every `<script>` a view renders, so no view handles it. With `script-src` naming
  only the nonce, an injected script does not run even if it points at a file on the portal. There is no
  `'unsafe-inline'` or `'unsafe-eval'` anywhere. `form-action` includes WSO2 because sign-out posts to the portal,
  which redirects to WSO2's logout endpoint. Referrers go to the portal only (`same-origin`): URLs carry return ids.
- **Views stay free of inline code.** A unit test scans every `.cshtml` for a `<script>` without `src`, `<style>`,
  `style="…"`, `on…=` handlers and `javascript:` URLs. Page scripts live in `wwwroot/js` (already the convention), and
  Chart.js styles its canvas through the CSS object model, which `style-src` does not restrict.
- **The API sends the strictest policy**: `default-src 'none'; base-uri 'none'; form-action 'none';
  frame-ancestors 'none'` with `Referrer-Policy: no-referrer`. Only Swagger UI under `/swagger` gets its own policy:
  scripts from the API only, inline styles (its bundle adds style elements), and `connect-src` to WSO2 for the
  token request.
- **HSTS** for 365 days with subdomains on both hosts (`UseHsts`, not on localhost), and again at the edge.
- **Signed-in answers are not cached.** When the user is authenticated and the endpoint set no `Cache-Control`, the
  answer gets `no-store`, so a shared computer's back button or a proxy does not keep bank figures.
- **Anti-forgery on every form.** The portal already has `AutoValidateAntiforgeryToken` as a global filter. An
  integration test now posts to every POST endpoint without a token, as a user every policy admits, and expects 400;
  only WSO2's back-channel logout (server to server, with a signed logout token) is exempt, and a second test fails
  if any other endpoint opts out. No action carries its own `[ValidateAntiForgeryToken]` (a third test): CodeQL's
  CSRF query does not recognise ASP.NET Core's global filters, and one such attribute anywhere makes it report every
  other form as unprotected. Another test lists the anonymous endpoints exactly (public pages, sign-in pages,
  back-channel logout, health), so a new `[AllowAnonymous]` is a visible decision.

### Failed sign-ins lock the account

- WSO2 7.3 ships with account locking off, so a password could be guessed without limit. IamBootstrap's new
  `account-lock` step turns it on: after `IamBootstrap:FailedSignInsBeforeLock` (5) wrong passwords or one-time codes
  in a row the account is locked for `IamBootstrap:AccountLockMinutes` (5), at most 60 guesses an hour per account.
  The lock does not grow with repeated locks (timeout ratio 1): the demo accounts' passwords are public, and a
  growing lock would let one visitor shut a demo role out for hours. WSO2's lock e-mails are off (no mail server).
- WSO2 answers a locked account with the same "login failed" message as a wrong password, so the lock does not reveal
  which user names exist. Checked live on 2026-10-09 with a throwaway account: after five wrong passwords the right
  one was refused and SCIM showed `accountLocked: true` with `MAX_ATTEMPTS_EXCEEDED`.

### SQL Server over TLS, validated by every client

- `scripts/dev-certs.sh` issues SQL Server a certificate from the development CA (CN and SAN `sqlserver`,
  `localhost`, `127.0.0.1`; `serverAuth`), in `.certs/sqlserver`. A one-off compose job (`sqlserver-tls`) installs
  it into the data volume, owned by the `mssql` user with the key readable by it alone, with an `mssql.conf` that sets
  `forceencryption = 1`. A client that does not encrypt is refused.
- Every client validates the certificate and uses TDS 8 strict encryption, in which TLS wraps the whole session,
  login included:
  - .NET: `Encrypt=Strict;ServerCertificate=<path to mssql.crt>`, which pins that exact certificate. No connection
    string uses `TrustServerCertificate` any more (`scripts/dev-secrets.sh`, the smoke tests).
  - WSO2's JDBC URLs: `encrypt=strict;serverCertificate=/home/wso2carbon/regreturns-sql/mssql.crt`. The JDBC driver
    connects before WSO2 has loaded its truststore (the error is "the trustAnchors parameter must be non-empty"), so
    the certificate is pinned rather than trusted through a store.
  - `sqlcmd` (health check, `wso2-db-init`): `-Ns -J <certificate>`.
  - The integration tests' Testcontainers SQL Server gets a self-signed certificate generated per run and the same
    `mssql.conf`, and connects with `Encrypt=Strict` and that certificate pinned, so tests run what production runs.
- Checked on 2026-10-09: every session (portal, migrator, WSO2, `sqlcmd`) reports `encrypt_option = TRUE` and
  protocol TDS 8. A client without the certificate fails validation, also when it asks for optional encryption (the
  server forces it), and a client that pins a different certificate is refused.

### The edge: deny by default

- `deploy/caddy/Caddyfile` (used from phase 11) fronts the portal, the API, WSO2 and Seq, every name a required
  placeholder with no default. On the WSO2 name only the end-user sign-in surface is public: `/oauth2/*`, `/oidc/*`,
  `/authenticationendpoint/*`, `/commonauth*` and `/logincontext*`. Everything else (Console, Carbon, management
  APIs, SCIM, My Account, account recovery, and anything a WSO2 upgrade adds) answers 403 unless the client's address
  is on `WSO2_ADMIN_ALLOWLIST`; an empty allowlist admits nobody. Paths containing `;` are never public, because
  Tomcat strips `;parameters` before resolving `..` (`/oauth2/..;/console`). Seq uses the same allowlist.
- The edge verifies WSO2's certificate against the RegReturns CA only, adds HSTS and removes `Server` on every answer
  (its own errors included), and writes JSON access logs with `code`, tokens and `client_secret` redacted in the URL,
  `Referer` and `Location`.
- `scripts/check-caddy.sh` validates the file and runs it unchanged against stub upstreams in Docker, with 49 checks
  (allowlist in and out, path tricks, a WSO2 certificate from a foreign CA, log redaction). CI runs it.
- Because `/scim2` is closed at the edge, the portal reaches WSO2's SCIM API through the back channel (ADR 0032).

### Logs

- The redaction safety net (ServiceDefaults `SensitiveData`) also masks `id_token_hint`, `logout_token`, `code`,
  `client_assertion`, `credential(s)`, `private_key`, `hmac_key` and `passcode` properties, with tests that codes such
  as `ErrorCode` and `StatusCode` stay readable. Request logs record the path only, never the query string (stated
  explicitly in `UseSerilogRequestLogging`). OpenTelemetry's ASP.NET Core and HttpClient instrumentation redact query
  values by default (1.19). Every `[LoggerMessage]` template was reviewed: none takes a secret, a figure or an e-mail;
  people appear by subject id, and the new authenticator-reset logs by WSO2 id.

### Supply chain

- CodeQL (`.github/workflows/codeql.yml`) analyses C# and the workflow files with the `security-extended` queries on
  every push to `main`, every pull request and weekly; actions are pinned by commit SHA. The vulnerable-package job
  (phase 1) fails on high or critical advisories.

### Diagnostics for administrators

- `/admin/diagnostics` (`AdminManage`: system administrator with TOTP) shows each health check with its status,
  duration and error (cut to 300 characters); the build, runtime, process and environment; the database's server
  version, the encryption of the portal's own session ("encrypted, TDS 8") next to what the connection string asks
  for ("Encrypt=Strict, certificate pinned"), the applied and pending migrations; the head of the audit chain; how
  many people are active and disabled; and the settings that matter for troubleshooting. Settings show whether a
  secret is set, never its value, and the telemetry endpoint as an origin only. A test checks that the page contains
  none of the configured secrets.

## Consequences

- An injected `<script>` or inline handler does not run in any supported browser, and the portal cannot be framed.
  A new inline script or style fails a unit test; a page that loads something the policy forbids shows up as a CSP
  violation in `scripts/demo-scenario.sh`, which now fails on any.
- Swagger UI keeps `'unsafe-inline'` for styles only. It is a developer tool on the API's origin, which holds no
  cookies, so a style injection there has nothing to read.
- Local setup needs `scripts/dev-certs.sh` before `docker compose up` (it already did for WSO2); an existing data
  volume picks up the certificate on the next `docker compose up`. `sqlcmd` calls need `-Ns -J`, and SQL tools such as
  SSMS or Azure Data Studio need the development CA trusted, or the certificate given, to connect.
- Production needs a certificate whose name matches the server name clients use; phase 11 issues it from the same CA
  on the server and mounts it read-only.
- ADR 0014's note on `TrustServerCertificate` is superseded by this record.
- Anyone who knows a user name can lock that account for five minutes by typing wrong passwords on the public
  sign-in page, which may include WSO2's own administrator. That is the price of the guessing limit; the lock is short
  and a setting, and phase 11 can give the WSO2 administrator a user name that is not guessable.
- The edge allowlist relies on Caddy seeing the client's real address. If a CDN or load balancer is ever put in
  front, it must switch to `client_ip` with that proxy declared in `trusted_proxies`.
