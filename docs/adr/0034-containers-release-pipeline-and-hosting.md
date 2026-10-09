# 34. Containers, a release pipeline that tests the production stack, and one server run by scripts

- Status: accepted
- Date: 2026-10-09

## Context

Phase 11 (plan §9 and §10) puts RegReturns on one internet-facing server of about 8 GB: the portal, the API, WSO2
Identity Server, SQL Server Express, Seq and Caddy. It has to be repeatable (a fresh server from a script, a release
from a commit), safe on the internet (phase 10, ADR 0033), observable, and recoverable from a lost server. One person
runs it, so every routine task must be a single command, and nothing may depend on remembering a manual step.

## Decision

### Images

- **One multi-stage `Dockerfile`** compiles the solution once and publishes four targets: `web`, `api`, `migrator`
  and `iam-bootstrap`. Restore runs from the project and lock files alone (`--locked-mode`), so that layer changes only
  with a dependency. The build context is an allowlist (`.dockerignore`): no `.env`, certificates, `bin/` or
  `appsettings.Development.json` can reach an image.
- **Runtime: `mcr.microsoft.com/dotnet/aspnet:<patch>-noble-chiseled-extra`** for all four (the tools use
  ServiceDefaults, which needs ASP.NET Core). Chiselled Ubuntu has no shell and no package manager and runs as the
  non-root `app` user (uid 1654); "extra" adds ICU and time zone data for SQL Server's client and the reports.
  Publishing keeps only the image's own platform under `runtimes/` (about 100 MB less per image). Images are
  360 to 390 MB.
- **No shell, so the app is its own health probe.** `--health-probe [path]` (ServiceDefaults `HealthProbe`) is
  answered before the host is built: it GETs `http://127.0.0.1:<ASPNETCORE_HTTP_PORTS>/health/ready` and exits 0 or 1.
  Docker's `HEALTHCHECK` runs it, so `docker compose up --wait` waits until the portal and the API can reach the
  database and WSO2.
- **The commit is stamped in** (`SOURCE_REVISION` → `SourceRevisionId`, shown on the status page, and the OCI
  `revision` label). Release images are tagged with the full commit sha; `main` is a moving convenience tag only.
- **WSO2 and its database job are images too** (`deploy/wso2`, `deploy/wso2/db-init`), built and tested with the
  rest, so the server never builds anything.
- **Every third-party image is pinned by tag**, CI tools by digest. Dependabot proposes base-image updates
  (`docker` and `docker-compose` ecosystems), and `ImagePinTests` fails when copies of one pin drift apart (SQL Server
  in both compose files, the WSO2 job and the test fixture; WSO2 in its two images and `dev-certs.sh`; Caddy in the
  production compose file and `check-caddy.sh`).

### The production stack (`docker-compose.prod.yml`)

- **Only Caddy publishes ports** (80, 443, 443/udp). Two networks: `edge` (Caddy, portal, API, WSO2, Seq) with a fixed
  subnet, and `backend` (`internal: true`, no route out) for SQL Server and server-to-server traffic.
- **Every container** drops all Linux capabilities and cannot gain privileges; the apps' root file systems are
  read-only with a `tmpfs` for `/tmp`; logs rotate (10 MB × 5); memory is capped as plan §9 sized it. Caddy, Seq and
  SQL Server get back exactly `NET_BIND_SERVICE` (the SQL Server and Seq binaries carry that file capability, and Linux
  refuses to start a binary whose file capabilities are outside the bounding set). Seq runs as its image's own user:
  its image starts as root and relies on root overriding file permissions, which dropping every capability removes.
- **Behind the proxy.** The apps believe `X-Forwarded-For` and `X-Forwarded-Proto` only from the networks in
  `ReverseProxy:KnownNetworks` (the edge subnet); set, it also moves the HTTPS redirect to the edge, so health checks
  and server-to-server calls on the internal network stay plain HTTP. The audit trail and rate limits therefore see the
  client's address, and HSTS and secure cookies see HTTPS. WSO2 is told its public URL (`base_path`) and port
  (`proxyPort`), so its issuer and every URL it hands out are `https://<IAM_HOST>/…` while the apps reach it directly
  at `https://wso2:9443` (`Wso2:BackchannelAuthority`). WSO2 7 continues a sign-in at the tenant-qualified
  `/t/carbon.super/oauth2/authorize`, which ADR 0033's edge did not list (its stub tests could not know): the public
  sign-in paths now include the `/t/carbon.super/` forms, and the deploy's smoke test follows the portal's sign-in
  redirect to WSO2's login page so a refused path fails the deploy.
- **The portal's data-protection keys** live on a volume (`DataProtection:KeysPath`) under a fixed application name,
  so a release or restart signs nobody out. They are stored unencrypted: they protect cookies, not data at rest, and
  the volume is readable only by the app's user and root.
- **Least-privilege database login.** The portal and the API log in as `regreturns_app`, whose only role is
  `regreturns_runtime` (migration `AddRuntimeRole`): read and write data and execute, but no schema changes, no
  switching off the audit trigger, and an explicit `DENY UPDATE, DELETE` on `audit.AuditEntries`, so the audit chain
  is append-only twice over. The `app-db-init` job creates the login and maps its user on every deploy and after a
  restore; it fails if anyone added the login to another role. The migrator, IamBootstrap and the backups use `sa`.
- **Seq runs on the server** (logs and traces over OTLP on the internal network), behind the same Caddy allowlist as
  the WSO2 console. The status page now also shows the REST API, checked over the internal network
  (`Status:ApiHealthUrl`) with a `status` tag that is not part of the portal's own readiness.

### Operations: `deploy/regreturns.sh` and `deploy/server-setup.sh`

- **`server-setup.sh`** turns a fresh Ubuntu 24.04 server into a host: Docker from Docker's repository (key
  fingerprint checked), unattended security updates, swap, ufw (SSH, HTTP, HTTPS), key-only SSH, the `regreturns`
  user with the checkout in `/srv/regreturns`, and two systemd timers.
- **`regreturns.sh`** is the one entry point: `init`, `deploy <sha>`, `backup`, `restore <stamp>`, `verify-audit`,
  `demo-users`, `smoke`, `status`, `sql`, `compose`. It reads `.env`, `generated/.env.generated` (IamBootstrap) and
  `.env.release` (the deployed tag) in that order and never puts a secret on a command line. `init` also gives a new
  server a WSO2 administrator name nobody can guess, which answers ADR 0033's account-lock concern.
- **A deploy** pulls the commit's images, starts SQL Server, runs `app-db-init`, applies migrations (and seeds the demo
  when it is on), starts WSO2, runs IamBootstrap `apply`, starts the rest with `--wait`, and smoke-tests every site
  through the local Caddy (TLS verified): readiness, the status page all green, the API refusing an anonymous call,
  WSO2's issuer, and 403 from the console and Seq. Rolling back is deploying the previous tag; migrations only go
  forward, so undoing one is a restore (docs/DR-RUNBOOK.md).
- **Backups run on host timers, not in a sidecar container** (the plan's first idea). A timer needs no long-running
  container holding the `sa` password, logs to the journal, catches up a missed run (`Persistent=true`), and uses the
  same script an operator runs by hand. Nightly at 02:30 UTC: `BACKUP DATABASE … WITH CHECKSUM` for the portal's and
  WSO2's three databases, `RESTORE VERIFYONLY`, then each file is streamed out of the container to
  `backups/<UTC stamp>/` (mode 0700, no bind mount into SQL Server) with a `SHA256SUMS` manifest; 14 days are kept.
  SQL Server Express has neither SQL Agent nor backup compression, so neither is assumed.
- **Off-site copies are always encrypted.** When `BACKUP_AGE_RECIPIENT` and `BACKUP_RCLONE_REMOTE` are set, each
  backup is streamed with the configuration it needs (`.env` with the audit HMAC key and WSO2's encryption key,
  generated secrets, certificates and keystores, the release) through `age` to any rclone remote. The private age key
  never lives on the server.
- **A restore** works on the same or a new server: it checks the checksums, stops the apps, restores with `REPLACE` and
  `CHECKSUM`, maps the database users to this server's logins again (both init jobs), applies newer migrations, and
  starts nothing unless `verify-audit` (a new migrator command, exit 0 intact, 2 broken) finds the chain intact. Then
  IamBootstrap reconciles WSO2 and writes the client secrets the restored WSO2 holds.
- **Targets: RPO 24 hours, RTO 2 hours** (the runbook's steps for a lost server). Hourly log backups would lower the
  RPO but need the full recovery model and a log chain to restore; for a demo that resets nightly they are not worth
  it, and the runbook says how to add them.

### Release pipeline (`.github/workflows/release.yml`)

- **Build once, test what ships.** On every pull request and push to main, one job builds the six images, writes a
  CycloneDX SBOM for each (Trivy, kept as a workflow artifact), and fails on any fixable high or critical
  vulnerability in our four images, in the OS or NuGet packages. WSO2's images are reported, not gated: they follow
  WSO2's releases, the findings are in its Ubuntu base (Java does not use the system OpenSSL), and only the sign-in
  paths are public.
- **End to end on the runner**, with the production compose file and scripts unchanged: `regreturns.sh init` and
  `deploy` with `*.localhost` names (Caddy's internal CA), the full identity smoke test with a browser
  (`smoke-wso2.sh --browser`, now able to trust the system store or Caddy's CA), then a backup, a change, a restore
  that must take the change away, and the smoke test again against the restored WSO2.
- **Only then, on main**, the tested images are pushed to GHCR (commit sha and `main`).
- **Deploys are gated and narrow.** The deploy job runs only when the repository variable `DEPLOY_HOST` is set, in the
  `production` environment. It connects with an SSH key that the server restricts to one forced command
  (`regreturns.sh ci-deploy`), checks the host key against a pinned `known_hosts`, and sends the run's `GITHUB_TOKEN`
  on stdin. The server accepts only `deploy <40-hex sha> <user>`, only for a commit on main, logs in to GHCR with a
  throwaway Docker config (the token expires with the run and is never stored), checks out that commit and runs that
  commit's script. GHCR packages can stay private.
- **Supply chain.** Only GitHub's own actions, pinned by commit; tools run as images pinned by digest: Trivy 0.69.3
  (published before the March 2026 compromise of 0.69.4, CVE-2026-33634), gitleaks, ShellCheck. CI gains a
  gitleaks scan of the whole history (`.gitleaks.toml` allows two named test values) and ShellCheck over every script.

## Consequences

- A release is a commit: what runs on the server is exactly what CI built, scanned and ran end to end, and the status
  page shows which commit that is.
- The deploy user is in the `docker` group, which is root-equivalent on the host. Rootless Docker would avoid that at
  the cost of port and cgroup workarounds; the server runs nothing else, SSH is key-only, and the CI key can run one
  command.
- Docker publishes Caddy's ports around ufw. That is acceptable because Caddy is the only publisher; adding a published
  port elsewhere would bypass the firewall.
- The internal CA from `scripts/dev-certs.sh` (still named "dev") is the server's private CA for WSO2's and SQL
  Server's certificates. It never leaves the server except inside the encrypted off-site archive; browsers see Let's
  Encrypt certificates from Caddy.
- Losing the configuration is as bad as losing the data: WSO2's encrypted secrets need its key from `.env`, and the
  audit chain needs the HMAC key. The off-site archive carries both, encrypted; the runbook says to test a restore.
- Not backed up, on purpose: Seq's data (logs, not records), Caddy's certificates (re-issued) and the portal's
  data-protection keys (people sign in again). The four databases are backed up one after another, so a change made
  during the backup can be in one and not another; at 02:30 UTC on a demo nobody is working.
- The end-to-end job makes each pull request take longer (images, WSO2 start, browser, restore), in exchange for
  testing the deploy and restore scripts on every change rather than on the day they are needed.
- The first real deploy still needs what only the owner can give: the domain, its DNS records and the server.
