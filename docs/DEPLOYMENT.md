# Deploying RegReturns

How the hosted demo runs: one Ubuntu 24.04 server with about 8 GB of memory, Docker Compose, Caddy in front, and
releases from GitHub Actions. The decisions behind it are in [ADR 0034](adr/0034-containers-release-pipeline-and-hosting.md);
recovering from a lost server is in [DR-RUNBOOK.md](DR-RUNBOOK.md).

```
            internet ── 80/443 ──► Caddy ─┬─► web:8080   (portal, PORTAL_HOST)
                                          ├─► api:8080   (REST API, API_HOST)
                                          ├─► wso2:9443  (sign-in paths for all; console for the allowlist, IAM_HOST)
                                          └─► seq:80     (logs and traces, allowlist only, SEQ_HOST)
   edge network (172.30.0.0/24) ─────────────────────────────────────────────────────────────────────────────
   backend network (internal) ──► sqlserver:1433  (RegReturns + WSO2's three databases, TLS pinned)
```

## What you need

- A server: Ubuntu 24.04, 2 or more vCPUs, 8 GB of memory, 40 GB of disk, a public IPv4 address.
- A domain with four names pointing at the server (A records, and AAAA if the server has IPv6), for example
  `regreturns.example.org`, `api.regreturns.example.org`, `iam.regreturns.example.org`, `seq.regreturns.example.org`.
  Caddy gets their certificates from Let's Encrypt on first start, so ports 80 and 443 must be reachable.
- Your public IP address (or range) for the administrator allowlist.
- An SSH key pair for yourself and, to deploy from GitHub Actions, a second one for CI (`ssh-keygen -t ed25519`).

## 1. Prepare the server (once, as root)

```bash
curl -fsSLO https://raw.githubusercontent.com/rovindu12/regreturns-portal/main/deploy/server-setup.sh
less server-setup.sh     # read it first
sudo bash server-setup.sh --admin-key "$(cat ~/.ssh/id_ed25519.pub)" --ci-key "ssh-ed25519 AAAA... regreturns-ci"
```

It installs Docker, git, jq, age and rclone, turns on unattended security updates, adds 2 GB of swap, opens only SSH,
HTTP and HTTPS, switches SSH to keys only, creates the `regreturns` user with the checkout in `/srv/regreturns`, and
installs two timers: the backup at 02:30 UTC and the demo users' reset at 03:10 UTC. The CI key can only run
`regreturns.sh ci-deploy` (`restrict,command=...` in `authorized_keys`).

## 2. Configure (once, as regreturns)

```bash
ssh regreturns@<server>
cd /srv/regreturns
deploy/regreturns.sh init
```

`init` creates `.env` with random secrets (`scripts/init-env.sh`) and a WSO2 administrator name nobody can guess, then
the server's private CA, SQL Server's certificate and WSO2's keystores (`scripts/dev-certs.sh`). Edit `.env`:

| Setting | Value |
|---|---|
| `PORTAL_HOST`, `API_HOST`, `IAM_HOST`, `SEQ_HOST` | the four names from DNS |
| `WSO2_ADMIN_ALLOWLIST` | your address as CIDR, e.g. `203.0.113.7/32`; empty admits nobody (use an SSH tunnel) |
| `DEMO_ENABLED` | `true` for the public demo (published accounts, nightly reset), `false` otherwise |
| `ANTHROPIC_API_KEY` | optional; without it fixed rules write the advisory insights (ADR 0030) |
| `BACKUP_AGE_RECIPIENT`, `BACKUP_RCLONE_REMOTE` | optional off-site backups (below) |

Keep a copy of `.env` somewhere safe off the server: without its audit HMAC key the audit chain cannot be verified,
and without WSO2's encryption key WSO2's secrets cannot be read.

## 3. Deploy

The first deploy, and any by hand, names a commit whose images the release workflow pushed (a commit on `main` with
a green **Release** run):

```bash
deploy/regreturns.sh deploy <commit sha>
```

It pulls the images, starts SQL Server, creates the database and the apps' login (`regreturns_app`, member of
`regreturns_runtime` only), applies migrations and seeds the demo, starts WSO2, runs IamBootstrap (`generated/` gets
the client secrets and the demo TOTP secrets), starts the portal, the API, Seq and Caddy, and checks every site
through the local Caddy. It ends with `Deployed <sha>.` or with the command that rolls back.

The GHCR packages are private by default. For a manual deploy, log in first with a token that can read them
(`docker login ghcr.io`), or make the packages public in GitHub (Packages → package settings).

## 4. Deploy from GitHub Actions

On every push to `main`, **Release** builds, scans and tests the images, pushes them and, once a server is configured,
deploys them. Configure in GitHub (Settings → Environments → `production`, and Settings → Variables):

| Kind | Name | Value |
|---|---|---|
| Variable | `DEPLOY_HOST` | the server's name or address; leaving it unset skips the deploy |
| Variable | `DEPLOY_USER` | `regreturns` (the default) |
| Variable | `PORTAL_URL` | `https://<PORTAL_HOST>`, checked after the deploy and shown on the run |
| Secret | `DEPLOY_SSH_KEY` | the CI private key (its public key went to `server-setup.sh --ci-key`) |
| Secret | `DEPLOY_KNOWN_HOSTS` | the server's host key line: `ssh-keyscan -t ed25519 <server>`, checked against the server's own fingerprint |

Add a required reviewer to the `production` environment to approve each deploy. The workflow sends its own
short-lived `GITHUB_TOKEN` over SSH on stdin; the server uses it once to pull and never stores it.

## Everyday commands

| Command | What it does |
|---|---|
| `deploy/regreturns.sh status` | deployed commit, containers and health, latest backup |
| `deploy/regreturns.sh smoke` | the post-deploy checks, any time |
| `deploy/regreturns.sh compose logs -f web` | follow a service's log (any `docker compose` command works) |
| `deploy/regreturns.sh backup` | a backup now (the timer runs one nightly) |
| `deploy/regreturns.sh verify-audit` | walk the audit hash chain; exit 2 if it is broken |
| `deploy/regreturns.sh deploy <older sha>` | roll back the images (not the schema; see the runbook) |
| `deploy/regreturns.sh sql -d RegReturns -Q "..."` | sqlcmd as `sa` inside the SQL Server container |
| `systemctl list-timers 'regreturns-*'`, `journalctl -u regreturns-backup` | the timers and their last runs |

Logs and traces are in Seq at `https://<SEQ_HOST>` (from an allowlisted address; user `admin`, password
`SEQ_ADMIN_PASSWORD` from `.env`). Every answer carries its trace id in `X-Trace-Id`, so a user's error page leads to
the request in Seq. The public status page at `https://<PORTAL_HOST>/status` shows the portal, the database, WSO2 and
the API, and the deployed commit.

## Off-site backups

Backups stay on the server for `BACKUP_RETENTION_DAYS` (14). For copies off the server, on your own machine:

```bash
age-keygen -o regreturns-backup.key   # keep this file safe and off the server; it prints the public key
```

On the server, configure an rclone remote (`rclone config`, for example an S3 bucket with object lock or versioning),
then set `BACKUP_AGE_RECIPIENT=age1...` (the public key) and `BACKUP_RCLONE_REMOTE=remote:bucket/regreturns` in `.env`.
Each nightly backup is then also uploaded as `regreturns-<stamp>.tar.age`: the database backups with `.env`, the
generated secrets, the certificates and the release, encrypted. Expire old copies with the storage's lifecycle rules.

## Security notes

- Only Caddy publishes ports. Docker publishes them around ufw, so never publish another one.
- The `regreturns` user is in the `docker` group, which is root-equivalent: keep SSH keys for it to administrators.
- The WSO2 console, its management APIs and Seq answer 403 to everyone off `WSO2_ADMIN_ALLOWLIST` (ADR 0033).
- The apps trust forwarded headers only from the edge network (`EDGE_SUBNET`); change it only if it clashes with a
  network on the server, and then everywhere at once (`.env` is the one place).
