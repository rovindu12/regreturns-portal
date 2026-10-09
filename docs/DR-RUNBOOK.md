# Disaster recovery runbook

What to do when data or the server is lost. Targets: **RPO 24 hours** (the nightly backup) and **RTO 2 hours** (a new
server, restored and serving). Background: [ADR 0034](adr/0034-containers-release-pipeline-and-hosting.md),
[DEPLOYMENT.md](DEPLOYMENT.md).

## What is backed up

| What | Where | How |
|---|---|---|
| `RegReturns`, `WSO2_IDENTITY_DB`, `WSO2_SHARED_DB`, `WSO2_AGENT_DB` | `backups/<UTC stamp>/<db>.bak` | nightly 02:30 UTC full backup with page checksums, verified with `RESTORE VERIFYONLY`, `SHA256SUMS` and `MANIFEST` (stamp, image tag) |
| The same, plus `.env`, `generated/.env.generated`, `.certs/`, `.env.release` | `BACKUP_RCLONE_REMOTE/regreturns-<stamp>.tar.age` | only when off-site backups are configured; encrypted with `age` |

Not backed up, on purpose: Seq's logs and traces, Caddy's certificates (re-issued on start), the portal's
data-protection keys (people sign in again) and the images (in GHCR by commit).

`.env` is as important as the backups: the audit chain cannot be verified without `AUDIT_HMAC_KEY`, and WSO2 cannot
read its secrets (TOTP keys, client secrets) without `WSO2_ENCRYPTION_KEY` and the keystores in `.certs/wso2`.

## Check the backups (monthly)

```bash
deploy/regreturns.sh status                       # latest backup and its age
journalctl -u regreturns-backup --since -2d       # last runs
cd backups/<stamp> && sha256sum -c SHA256SUMS      # files intact
rclone ls "$(grep ^BACKUP_RCLONE_REMOTE= .env | cut -d= -f2-)" | tail   # off-site copies arriving
```

A backup that is never restored is a hope, not a backup: the release workflow restores one on every change (backup,
change, restore, smoke test), and once a quarter restore the latest off-site copy onto a scratch server with the
steps below.

## Restore on the same server

For a bad migration, deleted data or a damaged database. Pick the newest backup from before the problem:

```bash
ls backups/
deploy/regreturns.sh restore <stamp>     # asks you to type the stamp; --yes for scripts
```

It checks the checksums, stops Caddy, the apps and WSO2, restores all four databases with `REPLACE` and `CHECKSUM`,
maps the database users to this server's logins, applies migrations newer than the backup, verifies the audit chain
(and stops there if it is broken), starts WSO2, lets IamBootstrap reconcile it, starts everything and runs the smoke
checks. Changes made after the backup are lost. If the deployed release is older than the backup's (`MANIFEST`
names its image tag), deploy that tag or a newer one first.

**Undoing a release with a schema change:** images roll back with `deploy/regreturns.sh deploy <previous sha>`, but
migrations only go forward. Restore the backup taken before the release, then deploy the previous commit.

## Restore on a new server (server lost)

About 2 hours, most of it waiting for DNS and WSO2.

1. **Server.** Create one as in [DEPLOYMENT.md](DEPLOYMENT.md) step 1 (`server-setup.sh` with your key and the CI
   key). Do not run `init`: the configuration comes from the backup.
2. **Get the archive** (on your machine, which has the age private key):

   ```bash
   rclone copy remote:bucket/regreturns/regreturns-<stamp>.tar.age .
   age --decrypt -i regreturns-backup.key regreturns-<stamp>.tar.age > regreturns-<stamp>.tar
   scp regreturns-<stamp>.tar regreturns@<new server>:/srv/regreturns/
   ```

   Without off-site copies, copy `backups/<stamp>/`, `.env`, `generated/`, `.certs/` and `.env.release` from wherever
   you kept them.
3. **Unpack** on the new server, as `regreturns`:

   ```bash
   cd /srv/regreturns
   tar -xf regreturns-<stamp>.tar && rm regreturns-<stamp>.tar
   git checkout --detach "$(grep ^IMAGE_TAG= .env.release | cut -d= -f2)"    # the scripts of that release
   docker login ghcr.io                                                      # unless the packages are public
   ```

4. **DNS.** Point the four names at the new server's address. Lower the TTL in advance if you can.
5. **Restore:** `deploy/regreturns.sh restore <stamp> --yes`. It pulls the release's images and does everything listed
   for a restore on the same server. Caddy requests new certificates once DNS points here; the smoke checks wait for
   them and fail with a hint if DNS has not moved yet (run `deploy/regreturns.sh smoke` again later).
6. **CI.** Update `DEPLOY_HOST` and `DEPLOY_KNOWN_HOSTS` in GitHub (the new host key), so the next push deploys here.
7. **Check:** `deploy/regreturns.sh status`, sign in to the portal as a demo user, and look at the status page.

## When the audit chain does not verify

`verify-audit` exits 2 and prints the first break: missing entries, a broken link or an edited entry (ADR 0016). The
restore stops before starting anything. Do not "fix" the chain. Try the previous backup; if every backup shows the
same break, the change predates them all: keep the database stopped, keep the backups, and investigate with the
auditor's view (`/audit` in a restored copy) before deciding what to tell the people who rely on the trail.

## Lowering the RPO

Nightly full backups give an RPO of 24 hours, which suits a demo that resets every night. For a real workload: switch
the databases to the full recovery model, add log backups every 15 to 60 minutes on another timer, and restore the
last full backup `WITH NORECOVERY` followed by the logs in order. SQL Server Express supports log backups; it has no
SQL Agent, so the host timer runs them.
