#!/usr/bin/env bash
# Operates the RegReturns production stack on a server (ADR 0034, docs/DEPLOYMENT.md, docs/DR-RUNBOOK.md). Run it as
# the deployment user (deploy/server-setup.sh creates "regreturns") from anywhere; it works in the checkout it lives in.
#
#   init                    first run: .env with random secrets, the internal CA, SQL Server's certificate and WSO2's
#                           keystores (scripts/dev-certs.sh), private generated/ and backups/ folders
#   deploy <tag> [--no-pull]
#                           runs the images built from <tag> (a commit sha): pull, start SQL Server, migrate (and seed
#                           the demo), start WSO2, IamBootstrap apply, start everything, smoke test. --no-pull uses
#                           images already loaded (the release workflow's end-to-end test)
#   ci-deploy               the forced command of the CI deploy key: accepts "deploy <sha> <github user>" from sshd,
#                           reads a registry token from stdin, checks out <sha> (a commit on main) and deploys it
#   backup                  full, checksummed and verified backups of every database into backups/<UTC stamp>, then
#                           retention and, when configured, an encrypted off-site copy with the configuration
#   restore <stamp> [--yes] restores those backups (on this or a new server), maps the logins again, applies newer
#                           migrations, verifies the audit chain, reconciles WSO2 and starts everything
#   verify-audit            walks the audit hash chain (exit 0 intact, 2 broken)
#   demo-users              resets the demo users in WSO2 after the nightly demo reset (systemd timer)
#   smoke                   checks the public sites through this server's Caddy
#   status                  containers, image tag and the latest backup
#   sql <sqlcmd args...>    sqlcmd as sa inside the sqlserver container, e.g. sql -d RegReturns -Q "SELECT 1"
#   compose <args...>       docker compose with the production file and env files, e.g. "compose logs -f web"
#
# Settings come from .env (scripts/init-env.sh, .env.example), generated/.env.generated (IamBootstrap) and .env.release
# (the deployed tag, written here), in that order. Secrets never appear on a command line or in the output.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE_FILE="${ROOT}/docker-compose.prod.yml"
ENV_FILE="${ROOT}/.env"
GENERATED="${ROOT}/generated/.env.generated"
RELEASE="${ROOT}/.env.release"
BACKUPS="${ROOT}/backups"
# Every database on the server: the portal's and WSO2's three (deploy/wso2/db-init).
DATABASES=(RegReturns WSO2_IDENTITY_DB WSO2_SHARED_DB WSO2_AGENT_DB)
# Inside the sqlserver container, on its volume.
SQL_BACKUP_DIR=/var/opt/mssql/backup
SQL_TLS_CERT=/var/opt/mssql/tls/mssql.crt
STAMP_PATTERN='^[0-9]{8}T[0-9]{6}Z$'

# IamBootstrap writes generated/.env.generated as the deployment user (docker-compose.prod.yml).
DEPLOY_UID="$(id -u)"
DEPLOY_GID="$(id -g)"
export DEPLOY_UID DEPLOY_GID

# Temporary folders of this run, removed on exit; and the release being deployed, for the rollback hint on failure.
CLEANUP=()
DEPLOYING=""
ROLLBACK_TO=""
on_exit() {
  local status=$?
  if [ "${status}" -ne 0 ] && [ -n "${DEPLOYING}" ]; then
    echo "The deploy of ${DEPLOYING} failed. Roll back with: deploy/regreturns.sh deploy ${ROLLBACK_TO:-<previous tag>}" >&2
    echo "(a schema migration it applied is undone only by a restore; see docs/DR-RUNBOOK.md)." >&2
  fi
  [ "${#CLEANUP[@]}" -eq 0 ] || rm -rf -- "${CLEANUP[@]}"
}
trap on_exit EXIT

log() { printf '%s  %s\n' "$(date -u +%H:%M:%SZ)" "$*"; }
fail() { echo "error: $*" >&2; exit 1; }
usage() { sed -n '2,/^set -euo/{/^set -euo/d;s/^# \{0,1\}//;p}' "${BASH_SOURCE[0]}" >&2; exit 2; }

# Value of KEY from the environment or .env, without sourcing the file (values may hold shell metacharacters).
setting() {
  local value="${!1:-}"
  if [ -z "${value}" ] && [ -f "${ENV_FILE}" ]; then
    value="$(grep -E "^$1=" "${ENV_FILE}" | tail -n 1 | cut -d= -f2- || true)"
  fi
  printf '%s' "${value}"
}

current_tag() { [ -f "${RELEASE}" ] && grep -E '^IMAGE_TAG=' "${RELEASE}" | cut -d= -f2- || true; }

compose() {
  local files=(--env-file "${ENV_FILE}")
  [ -f "${GENERATED}" ] && files+=(--env-file "${GENERATED}")
  [ -f "${RELEASE}" ] && files+=(--env-file "${RELEASE}")
  docker compose --project-directory "${ROOT}" -f "${COMPOSE_FILE}" "${files[@]}" "$@"
}

# One-off jobs and tools; the image is pulled only if this server does not have it yet.
run_job() { compose run --rm --pull missing "$@"; }

require_settings() {
  [ -f "${ENV_FILE}" ] || fail ".env is missing: run deploy/regreturns.sh init"
  local name missing=()
  for name in PORTAL_HOST API_HOST IAM_HOST SEQ_HOST MSSQL_SA_PASSWORD APP_DB_PASSWORD AUDIT_HMAC_KEY SEQ_ADMIN_PASSWORD; do
    [ -n "$(setting "${name}")" ] || missing+=("${name}")
  done
  [ "${#missing[@]}" -eq 0 ] || fail "set these in .env first: ${missing[*]} (see .env.example)"
  for name in PORTAL_HOST API_HOST IAM_HOST SEQ_HOST; do
    [[ "$(setting "${name}")" != *example.* ]] || fail "${name} is still the example name: set your own host names in .env"
  done
  for name in "${ROOT}/.certs/regreturns-dev-ca.crt" "${ROOT}/.certs/sqlserver/mssql.crt" "${ROOT}/.certs/wso2/regreturns-tls.p12"; do
    [ -f "${name}" ] || fail "${name#"${ROOT}"/} is missing: run deploy/regreturns.sh init"
  done
}

demo_enabled() { [ "$(setting DEMO_ENABLED | tr '[:upper:]' '[:lower:]')" != false ]; }

# sqlcmd inside the sqlserver container as sa: the password comes from the container's own environment.
sql() {
  compose exec -T sqlserver bash -c \
    'SQLCMDPASSWORD="${MSSQL_SA_PASSWORD}" exec /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -Ns -J '"${SQL_TLS_CERT}"' -b -l 30 "$@"' \
    sqlcmd "$@"
}

cmd_init() {
  local fresh=false
  [ -f "${ENV_FILE}" ] || fresh=true
  "${ROOT}/scripts/init-env.sh"
  if [ "${fresh}" = true ]; then
    # A WSO2 administrator name nobody can guess, so nobody can lock it out with wrong passwords (ADR 0033). It is
    # fixed at WSO2's first start, so only a new .env gets one.
    sed -i "s|^WSO2_ADMIN_USERNAME=.*|WSO2_ADMIN_USERNAME=iamadmin-$(openssl rand -hex 4)|" "${ENV_FILE}"
  fi
  install -d -m 0700 "${ROOT}/generated" "${BACKUPS}"
  "${ROOT}/scripts/dev-certs.sh"
  echo "Ready. Next: set the host names and WSO2_ADMIN_ALLOWLIST in .env, point DNS at this server, then"
  echo "deploy/regreturns.sh deploy <commit sha>."
}

cmd_deploy() {
  local tag="${1:-}" pull=true
  [[ "${tag}" =~ ^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$ ]] || fail "deploy needs an image tag, such as the commit sha"
  [ "${2:-}" = --no-pull ] && pull=false
  require_settings
  local previous
  previous="$(current_tag)"
  DEPLOYING="${tag}"
  ROLLBACK_TO="${previous}"

  export IMAGE_TAG="${tag}"
  if [ "${pull}" = true ]; then
    log "Pulling the images of ${tag}"
    compose --profile tools pull --quiet
  fi
  # The release in force from here on, for every later command (status, backup, restore, demo-users).
  printf 'IMAGE_TAG=%s\nPREVIOUS_IMAGE_TAG=%s\nDEPLOYED_AT=%s\n' "${tag}" "${previous}" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "${RELEASE}.new"
  mv -f "${RELEASE}.new" "${RELEASE}"

  log "Starting SQL Server"
  compose up -d --wait --pull missing sqlserver
  log "Preparing the database and the apps' login"
  run_job app-db-init
  log "Applying migrations"
  if demo_enabled; then run_job migrator migrate-db --seed; else run_job migrator migrate-db; fi
  log "Starting WSO2 Identity Server"
  compose up -d --wait --pull missing wso2
  log "Applying the WSO2 setup (IamBootstrap)"
  install -d -m 0700 "${ROOT}/generated"
  run_job iam-bootstrap apply
  log "Starting the portal, the API, Seq and Caddy"
  compose up -d --wait --pull missing --remove-orphans caddy web api seq
  cmd_smoke
  DEPLOYING=""
  if [ "${pull}" = true ]; then
    # Images of older releases stay for 30 days, for a quick rollback.
    docker image prune --all --force --filter "until=720h" > /dev/null
  fi
  log "Deployed ${tag}."
}

cmd_ci_deploy() {
  # sshd passes the client's command in SSH_ORIGINAL_COMMAND; anything else is refused.
  local request="${SSH_ORIGINAL_COMMAND:-}"
  [[ "${request}" =~ ^deploy\ ([0-9a-f]{40})\ ([A-Za-z0-9][A-Za-z0-9-]{0,38})$ ]] \
    || fail "refused: the deploy key only runs 'deploy <commit sha> <github user>'"
  local sha="${BASH_REMATCH[1]}" actor="${BASH_REMATCH[2]}" token
  # The workflow's GITHUB_TOKEN (read access to the packages, valid for its run only) arrives on stdin.
  IFS= read -r token || true
  [ -n "${token}" ] || fail "no registry token on stdin"

  log "Fetching ${sha}"
  git -C "${ROOT}" fetch --quiet origin main
  # Only commits on main, which the release workflow built and scanned; never one from a fork or another branch.
  git -C "${ROOT}" merge-base --is-ancestor "${sha}" origin/main || fail "${sha} is not a commit on main"
  git -C "${ROOT}" checkout --quiet --detach "${sha}"

  # A throwaway Docker config, so the token is never stored on the server.
  DOCKER_CONFIG="$(mktemp -d)"
  export DOCKER_CONFIG
  CLEANUP+=("${DOCKER_CONFIG}")
  printf '%s' "${token}" | docker login ghcr.io --username "${actor}" --password-stdin > /dev/null
  unset token
  # The checked-out version of this script deploys, so the scripts always match the compose file.
  "${ROOT}/deploy/regreturns.sh" deploy "${sha}"
}

cmd_backup() {
  require_settings
  local stamp dir database file
  stamp="$(date -u +%Y%m%dT%H%M%SZ)"
  dir="${BACKUPS}/${stamp}"
  umask 077
  install -d -m 0700 "${BACKUPS}" "${dir}.partial"
  log "Backing up ${DATABASES[*]} to backups/${stamp}"
  compose exec -T sqlserver mkdir -p "${SQL_BACKUP_DIR}/${stamp}"
  for database in "${DATABASES[@]}"; do
    file="${SQL_BACKUP_DIR}/${stamp}/${database}.bak"
    # Page checksums are checked while reading, and the finished file is read back once more before it counts.
    sql -v Db="${database}" -v File="${file}" -Q "
SET NOCOUNT ON;
BACKUP DATABASE [\$(Db)] TO DISK = N'\$(File)' WITH CHECKSUM, INIT, FORMAT, NAME = N'\$(Db) full', STATS = 100;
RESTORE VERIFYONLY FROM DISK = N'\$(File)' WITH CHECKSUM;" > /dev/null
    compose exec -T sqlserver cat "${file}" > "${dir}.partial/${database}.bak"
  done
  compose exec -T sqlserver rm -rf "${SQL_BACKUP_DIR:?}/${stamp}"
  (cd "${dir}.partial" && sha256sum -- *.bak > SHA256SUMS)
  printf 'stamp=%s\nimage_tag=%s\ndatabases=%s\n' "${stamp}" "$(current_tag)" "${DATABASES[*]}" > "${dir}.partial/MANIFEST"
  mv "${dir}.partial" "${dir}"
  log "Backup ${stamp} written and verified ($(du -sh "${dir}" | cut -f1))."

  offsite "${stamp}"
  prune_backups
}

# An encrypted copy off the server: the backups plus everything needed to use them (.env with the audit HMAC key and
# WSO2's encryption key, the generated client secrets, the certificates and keystores, the release). Only when both
# BACKUP_AGE_RECIPIENT (an age public key) and BACKUP_RCLONE_REMOTE (an rclone remote and path) are set; never
# unencrypted.
offsite() {
  local stamp="$1" recipient remote
  recipient="$(setting BACKUP_AGE_RECIPIENT)"
  remote="$(setting BACKUP_RCLONE_REMOTE)"
  if [ -z "${remote}" ]; then
    log "No off-site copy (BACKUP_RCLONE_REMOTE is not set)."
    return
  fi
  [ -n "${recipient}" ] || fail "BACKUP_RCLONE_REMOTE is set but BACKUP_AGE_RECIPIENT is not: off-site copies are always encrypted"
  local items=("backups/${stamp}" .env .certs)
  [ -f "${GENERATED}" ] && items+=(generated/.env.generated)
  [ -f "${RELEASE}" ] && items+=(.env.release)
  log "Copying an encrypted archive to ${remote}"
  tar -C "${ROOT}" -cf - -- "${items[@]}" | age --encrypt --recipient "${recipient}" \
    | rclone rcat --quiet "${remote%/}/regreturns-${stamp}.tar.age"
  log "Off-site copy regreturns-${stamp}.tar.age stored."
}

prune_backups() {
  local days
  days="$(setting BACKUP_RETENTION_DAYS)"
  days="${days:-14}"
  [[ "${days}" =~ ^[0-9]+$ ]] && [ "${days}" -ge 1 ] || fail "BACKUP_RETENTION_DAYS must be a whole number of days"
  # The newest backup is never pruned, whatever its age; unfinished ones go after a day.
  local newest
  newest="$(find "${BACKUPS}" -mindepth 1 -maxdepth 1 -type d -regextype posix-extended -regex '.*/[0-9]{8}T[0-9]{6}Z' | sort | tail -n 1)"
  find "${BACKUPS}" -mindepth 1 -maxdepth 1 -type d -regextype posix-extended -regex '.*/[0-9]{8}T[0-9]{6}Z' \
    -mtime +"$((days - 1))" ! -path "${newest}" -print -exec rm -rf -- {} + | sed 's|.*/|Pruned backup |'
  find "${BACKUPS}" -mindepth 1 -maxdepth 1 -type d -name '*.partial' -mtime +0 -exec rm -rf -- {} +
}

cmd_restore() {
  local stamp="${1:-}" confirmed="${2:-}"
  [[ "${stamp}" =~ ${STAMP_PATTERN} ]] || fail "restore needs a backup stamp such as 20261009T023000Z (ls backups)"
  local dir="${BACKUPS}/${stamp}" database file answer
  [ -f "${dir}/SHA256SUMS" ] || fail "backups/${stamp} is not a finished backup"
  require_settings
  [ -n "$(current_tag)" ] || fail ".env.release is missing: restore it from the off-site archive or run deploy first"
  log "Checking the files of backup ${stamp}"
  (cd "${dir}" && sha256sum --quiet --strict -c SHA256SUMS) || fail "backups/${stamp} is damaged: its checksums do not match"
  if [ "${confirmed}" != --yes ]; then
    [ -t 0 ] || fail "restore replaces every database: pass --yes to confirm"
    read -r -p "This replaces ${DATABASES[*]} with backup ${stamp}. Type the stamp to continue: " answer
    [ "${answer}" = "${stamp}" ] || fail "not confirmed"
  fi

  log "Stopping the apps, WSO2 and the edge"
  compose stop caddy web api wso2 > /dev/null 2>&1 || true
  compose up -d --wait --pull missing sqlserver
  compose exec -T sqlserver mkdir -p "${SQL_BACKUP_DIR}/restore-${stamp}"
  for database in "${DATABASES[@]}"; do
    [ -f "${dir}/${database}.bak" ] || fail "backups/${stamp} has no ${database}.bak"
    file="${SQL_BACKUP_DIR}/restore-${stamp}/${database}.bak"
    log "Restoring ${database}"
    compose exec -T sqlserver bash -c 'cat > "$1"' copy "${file}" < "${dir}/${database}.bak"
    sql -v Db="${database}" -v File="${file}" -Q "
SET NOCOUNT ON;
RESTORE VERIFYONLY FROM DISK = N'\$(File)' WITH CHECKSUM;
IF DB_ID(N'\$(Db)') IS NOT NULL ALTER DATABASE [\$(Db)] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
RESTORE DATABASE [\$(Db)] FROM DISK = N'\$(File)' WITH REPLACE, CHECKSUM, RECOVERY, STATS = 100;
ALTER DATABASE [\$(Db)] SET MULTI_USER;" > /dev/null
  done
  compose exec -T sqlserver rm -rf "${SQL_BACKUP_DIR:?}/restore-${stamp}"

  # On another server the database users are orphaned: the init jobs map them to this server's logins again.
  log "Mapping the database users to this server's logins"
  run_job app-db-init
  run_job wso2-db-init
  log "Applying migrations newer than the backup"
  run_job migrator migrate-db
  log "Verifying the audit chain"
  run_job migrator verify-audit || fail "the restored audit chain does not verify; nothing was started (see docs/DR-RUNBOOK.md)"
  log "Starting WSO2 and reconciling its setup"
  compose up -d --wait --pull missing wso2
  run_job iam-bootstrap apply
  compose up -d --wait --pull missing --remove-orphans caddy web api seq
  cmd_smoke
  log "Restored backup ${stamp}."
}

# Each public site through this server's Caddy (not DNS), with TLS verified against the system trust store, or against
# SMOKE_CA: a CA file, or "caddy" for the root of Caddy's internal CA, which signs *.localhost names (the release
# workflow's end-to-end test). Caddy may still be fetching certificates after a first start.
cmd_smoke() {
  local portal api iam seq ca=() attempt work
  portal="$(setting PORTAL_HOST)"; api="$(setting API_HOST)"; iam="$(setting IAM_HOST)"; seq="$(setting SEQ_HOST)"
  work="$(mktemp -d)"
  CLEANUP+=("${work}")
  if [ "${SMOKE_CA:-}" = caddy ]; then
    for attempt in $(seq 1 20); do
      compose exec -T caddy cat /data/caddy/pki/authorities/local/root.crt > "${work}/caddy-root.crt" 2> /dev/null && break
      [ "${attempt}" -eq 20 ] && fail "smoke: Caddy's internal CA root did not appear"
      sleep 2
    done
    ca=(--cacert "${work}/caddy-root.crt")
  elif [ -n "${SMOKE_CA:-}" ]; then
    ca=(--cacert "${SMOKE_CA}")
  fi
  get() { local host="$1" path="$2"; curl -sS -o "${3:-/dev/null}" -w '%{http_code}' --max-time 15 "${ca[@]}" \
    --resolve "${host}:443:127.0.0.1" "https://${host}${path}" 2> /dev/null || true; }
  expect() {
    local code
    code="$(get "$1" "$2" "${4:-/dev/null}")"
    [ "${code}" = "$3" ] || fail "smoke: https://$1$2 answered ${code:-nothing}, expected $3"
    log "smoke ok  https://$1$2 -> $3"
  }

  for attempt in $(seq 1 30); do
    [ "$(get "${portal}" /health/live)" = 200 ] && break
    [ "${attempt}" -eq 30 ] && fail "smoke: https://${portal} is not answering over TLS (DNS, certificates, Caddy logs?)"
    sleep 3
  done
  local body="${work}/body"
  expect "${portal}" /health/ready 200
  expect "${portal}" /status 200 "${body}"
  grep -q 'data-status="Operational"' "${body}" || fail "smoke: the status page does not say every component is operational"
  log "smoke ok  status page: all components operational"
  expect "${api}" /health/ready 200
  expect "${api}" /v1/me 401
  expect "${iam}" /oauth2/token/.well-known/openid-configuration 200 "${body}"
  [ "$(jq -r .issuer "${body}")" = "https://${iam}/oauth2/token" ] || fail "smoke: WSO2's issuer is not https://${iam}/oauth2/token"
  log "smoke ok  WSO2 issuer https://${iam}/oauth2/token"
  # Signing in: the portal writes its correlation cookie (data-protection keys) and sends the browser to WSO2, whose
  # authorize endpoint answers through Caddy's public sign-in paths with its login page.
  local answer
  answer="$(curl -sS -o /dev/null -w '%{http_code} %{redirect_url}' --max-time 15 "${ca[@]}" \
    --resolve "${portal}:443:127.0.0.1" "https://${portal}/Account/SignIn" 2> /dev/null || true)"
  [[ "${answer}" == "302 https://${iam}/oauth2/authorize?"* ]] \
    || fail "smoke: signing in to the portal does not lead to WSO2 (answered ${answer%%\?*})"
  answer="$(curl -sS -o /dev/null -w '%{http_code} %{redirect_url}' --max-time 15 "${ca[@]}" \
    --resolve "${iam}:443:127.0.0.1" "${answer#302 }" 2> /dev/null || true)"
  [[ "${answer}" == 302\ *login.do* ]] || fail "smoke: WSO2 does not show its login page (answered ${answer%%\?*})"
  log "smoke ok  signing in leads to WSO2's login page"
  # Administrator surfaces are refused to anyone off the allowlist, and this server is never on it.
  expect "${iam}" /console 403
  expect "${seq}" / 403
}

cmd_status() {
  echo "Release: $(current_tag || true) ($(grep -E '^DEPLOYED_AT=' "${RELEASE}" 2> /dev/null | cut -d= -f2- || echo never))"
  local latest
  latest="$(find "${BACKUPS}" -mindepth 1 -maxdepth 1 -type d -regextype posix-extended -regex '.*/[0-9]{8}T[0-9]{6}Z' 2> /dev/null | sort | tail -n 1)"
  if [ -n "${latest}" ]; then
    echo "Latest backup: ${latest##*/} ($(( ($(date +%s) - $(stat -c %Y "${latest}")) / 3600 )) h old, $(du -sh "${latest}" | cut -f1))"
  else
    echo "Latest backup: none"
  fi
  compose ps --format 'table {{.Service}}\t{{.Status}}\t{{.Image}}'
}

command="${1:-}"
[ -n "${command}" ] || usage
shift
case "${command}" in
  init) cmd_init ;;
  deploy) cmd_deploy "$@" ;;
  ci-deploy) cmd_ci_deploy ;;
  backup) cmd_backup ;;
  restore) cmd_restore "$@" ;;
  verify-audit) require_settings; run_job migrator verify-audit ;;
  demo-users)
    require_settings
    demo_enabled || { log "The demo is off (DEMO_ENABLED=false): no demo users to reset."; exit 0; }
    run_job iam-bootstrap demo-users
    # The portal shows the demo's TOTP secrets; it is recreated only if IamBootstrap had to enrol a new one.
    compose up -d --wait --pull missing web
    ;;
  smoke) require_settings; cmd_smoke ;;
  status) cmd_status ;;
  sql) require_settings; sql "$@" ;;
  compose) compose "$@" ;;
  -h|--help|help) usage ;;
  *) echo "Unknown command: ${command}" >&2; usage ;;
esac
