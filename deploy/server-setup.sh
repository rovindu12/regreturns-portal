#!/usr/bin/env bash
# Prepares a fresh Ubuntu 24.04 server for RegReturns (ADR 0034, docs/DEPLOYMENT.md). Run once, as root, after
# reading it:
#
#   curl -fsSLO https://raw.githubusercontent.com/rovindu12/regreturns-portal/main/deploy/server-setup.sh
#   sudo bash server-setup.sh --admin-key "ssh-ed25519 AAAA... you@laptop" [--ci-key "ssh-ed25519 AAAA... ci"]
#
# It installs Docker Engine (Docker's apt repository, key fingerprint checked), git, jq, age and rclone; turns on
# unattended security updates; adds swap; lets only SSH, HTTP and HTTPS in (ufw); allows SSH keys only; creates the
# deployment user "regreturns" (in the docker group) with the checkout in /srv/regreturns; and installs systemd timers
# for the nightly backup (02:30 UTC) and the demo users' reset (03:10 UTC, after the portal's 03:00 demo reset).
#
#   --admin-key KEY   an SSH public key that may sign in as regreturns to operate the stack (repeatable)
#   --ci-key KEY      the release workflow's deploy key: it can only run "regreturns.sh ci-deploy" (forced command)
#   --repo URL        the repository to clone (default https://github.com/rovindu12/regreturns-portal.git)
#   --swap SIZE       swap file size when the server has none (default 2G)
#
# Safe to run again: each step checks before it changes anything. Docker publishes Caddy's ports itself, around ufw;
# the compose file publishes nothing else.
set -euo pipefail

REPO="https://github.com/rovindu12/regreturns-portal.git"
APP_USER=regreturns
APP_DIR=/srv/regreturns
SWAP_SIZE=2G
ADMIN_KEYS=()
CI_KEY=""
# Docker's release signing key (https://docs.docker.com/engine/install/ubuntu/).
DOCKER_KEY_FINGERPRINT=9DC858229FC7DD38854AE2D88D81803C0EBFCD88

while [ $# -gt 0 ]; do
  case "$1" in
    --admin-key) ADMIN_KEYS+=("${2:?--admin-key needs a public key}"); shift 2 ;;
    --ci-key) CI_KEY="${2:?--ci-key needs a public key}"; shift 2 ;;
    --repo) REPO="${2:?--repo needs a URL}"; shift 2 ;;
    --swap) SWAP_SIZE="${2:?--swap needs a size such as 2G}"; shift 2 ;;
    -h|--help) sed -n '2,/^set -euo/{/^set -euo/d;s/^# \{0,1\}//;p}' "$0"; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

log() { printf '\n== %s\n' "$*"; }
fail() { echo "error: $*" >&2; exit 1; }
valid_key() { [[ "$1" =~ ^(ssh-ed25519|ecdsa-sha2-nistp256|ecdsa-sha2-nistp384|ecdsa-sha2-nistp521|ssh-rsa)\ [A-Za-z0-9+/=]+(\ [^\"]*)?$ ]]; }

[ "$(id -u)" -eq 0 ] || fail "run as root (sudo bash $0 ...)"
# shellcheck source=/dev/null
. /etc/os-release
[ "${ID}" = ubuntu ] && [ "${VERSION_ID}" = 24.04 ] || echo "warning: written for Ubuntu 24.04, this is ${PRETTY_NAME}" >&2
for key in "${ADMIN_KEYS[@]}" ${CI_KEY:+"${CI_KEY}"}; do
  valid_key "${key}" || fail "not an SSH public key: ${key:0:40}..."
done

export DEBIAN_FRONTEND=noninteractive

log "Packages and unattended security updates"
apt-get update -q
apt-get install -y -q ca-certificates curl git gnupg jq openssl age rclone ufw unattended-upgrades
cat > /etc/apt/apt.conf.d/20auto-upgrades <<'EOF'
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
EOF

log "Docker Engine"
if ! command -v docker > /dev/null; then
  install -m 0755 -d /etc/apt/keyrings
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc.new
  fingerprint="$(gpg --show-keys --with-colons /etc/apt/keyrings/docker.asc.new | awk -F: '$1 == "fpr" { print $10; exit }')"
  [ "${fingerprint}" = "${DOCKER_KEY_FINGERPRINT}" ] || { rm -f /etc/apt/keyrings/docker.asc.new; fail "Docker's apt key has an unexpected fingerprint: ${fingerprint}"; }
  mv /etc/apt/keyrings/docker.asc.new /etc/apt/keyrings/docker.asc
  chmod a+r /etc/apt/keyrings/docker.asc
  echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu ${VERSION_CODENAME} stable" \
    > /etc/apt/sources.list.d/docker.list
  apt-get update -q
  apt-get install -y -q docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
fi
if [ ! -f /etc/docker/daemon.json ]; then
  # Bounded container logs, containers that survive a daemon restart, and no privilege gain in any container.
  cat > /etc/docker/daemon.json <<'EOF'
{
  "log-driver": "json-file",
  "log-opts": { "max-size": "10m", "max-file": "5" },
  "live-restore": true,
  "no-new-privileges": true
}
EOF
  systemctl restart docker
else
  echo "/etc/docker/daemon.json exists; left unchanged"
fi
systemctl enable --now docker > /dev/null

log "Swap"
if [ -z "$(swapon --noheadings --show)" ]; then
  fallocate -l "${SWAP_SIZE}" /swapfile
  chmod 0600 /swapfile
  mkswap /swapfile > /dev/null
  swapon /swapfile
  grep -q '^/swapfile ' /etc/fstab || echo '/swapfile none swap sw 0 0' >> /etc/fstab
  echo 'vm.swappiness=10' > /etc/sysctl.d/90-regreturns-swap.conf
  sysctl -q -p /etc/sysctl.d/90-regreturns-swap.conf
else
  echo "Swap already present"
fi

log "Deployment user ${APP_USER}"
id "${APP_USER}" > /dev/null 2>&1 || useradd --create-home --shell /bin/bash "${APP_USER}"
usermod -aG docker "${APP_USER}"
home="$(getent passwd "${APP_USER}" | cut -d: -f6)"
install -d -o "${APP_USER}" -g "${APP_USER}" -m 0700 "${home}/.ssh"
keys="${home}/.ssh/authorized_keys"
touch "${keys}"
for key in "${ADMIN_KEYS[@]}"; do
  grep -qF "${key}" "${keys}" || echo "${key}" >> "${keys}"
done
if [ -n "${CI_KEY}" ]; then
  # The CI key runs one command and nothing else: no shell, terminal, forwarding or agent.
  ci_line="restrict,command=\"${APP_DIR}/deploy/regreturns.sh ci-deploy\" ${CI_KEY}"
  grep -qF "${CI_KEY}" "${keys}" || echo "${ci_line}" >> "${keys}"
fi
chown "${APP_USER}:${APP_USER}" "${keys}"
chmod 0600 "${keys}"

log "Checkout in ${APP_DIR}"
install -d -o "${APP_USER}" -g "${APP_USER}" -m 0750 "${APP_DIR}"
if [ ! -d "${APP_DIR}/.git" ]; then
  runuser -u "${APP_USER}" -- git clone --quiet "${REPO}" "${APP_DIR}"
fi
runuser -u "${APP_USER}" -- install -d -m 0700 "${APP_DIR}/backups" "${APP_DIR}/generated"

log "SSH: keys only"
# Only when someone can still sign in with a key afterwards: the operator's own account, root or the deployment user.
operator_home="$(getent passwd "${SUDO_USER:-root}" | cut -d: -f6)"
if [ -s "${operator_home}/.ssh/authorized_keys" ] || [ "${#ADMIN_KEYS[@]}" -gt 0 ]; then
  cat > /etc/ssh/sshd_config.d/10-regreturns.conf <<'EOF'
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitRootLogin prohibit-password
EOF
  sshd -t
  systemctl reload ssh
else
  echo "warning: no authorized SSH key found, so password sign-in was left on; add a key and run this again" >&2
fi

log "Firewall"
ufw default deny incoming > /dev/null
ufw default allow outgoing > /dev/null
ufw allow OpenSSH > /dev/null
ufw allow 80/tcp > /dev/null
ufw allow 443/tcp > /dev/null
ufw allow 443/udp > /dev/null
ufw --force enable > /dev/null
ufw status verbose | head -n 12

log "systemd timers"
unit() {
  local name="$1" description="$2" command="$3" calendar="$4"
  cat > "/etc/systemd/system/regreturns-${name}.service" <<EOF
[Unit]
Description=RegReturns: ${description}
After=docker.service network-online.target
Wants=network-online.target
Requires=docker.service

[Service]
Type=oneshot
User=${APP_USER}
Group=${APP_USER}
WorkingDirectory=${APP_DIR}
ExecStart=${APP_DIR}/deploy/regreturns.sh ${command}
NoNewPrivileges=yes
PrivateTmp=yes
ProtectSystem=full
EOF
  cat > "/etc/systemd/system/regreturns-${name}.timer" <<EOF
[Unit]
Description=RegReturns: ${description} (${calendar})

[Timer]
OnCalendar=${calendar}
Persistent=true
RandomizedDelaySec=120

[Install]
WantedBy=timers.target
EOF
}
unit backup "nightly database backup" backup "*-*-* 02:30:00 UTC"
unit demo-users "reset the demo users in WSO2" demo-users "*-*-* 03:10:00 UTC"
systemctl daemon-reload
systemctl enable --now regreturns-backup.timer regreturns-demo-users.timer > /dev/null
systemctl list-timers 'regreturns-*' --no-pager

cat <<EOF

Done. Next, as ${APP_USER} (ssh ${APP_USER}@<server>):
  cd ${APP_DIR} && deploy/regreturns.sh init
  edit .env: PORTAL_HOST, API_HOST, IAM_HOST, SEQ_HOST, WSO2_ADMIN_ALLOWLIST, optional BACKUP_* settings
  point the four names at this server in DNS, then: deploy/regreturns.sh deploy <commit sha>
See docs/DEPLOYMENT.md.
EOF
