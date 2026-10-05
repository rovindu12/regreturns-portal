#!/usr/bin/env bash
# Creates .env from .env.example with random secrets, so a fresh clone never runs on example passwords.
#
# On an existing .env it never changes a value already set: the SQL Server volume, the keystores and WSO2's database
# hold those secrets, so rotating one here would lock the stack out. It only
#   - restricts the file to its owner (mode 600),
#   - adds keys that .env.example has and .env lacks (new settings from a later version), with random secrets,
#   - fails if a secret is still the published example value, naming the keys to rotate (docs/TROUBLESHOOTING.md).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TARGET="${ROOT}/.env"
EXAMPLE="${ROOT}/.env.example"

# Strong password that meets SQL Server's policy and contains no quotes or shell metacharacters.
password() { printf '%s_Aa1' "$(openssl rand -base64 24 | tr -dc 'A-Za-z0-9' | head -c 24)"; }

endpoint_password="$(password)"
declare -A secrets=(
  [MSSQL_SA_PASSWORD]="$(password)"
  [WSO2_ADMIN_PASSWORD]="$(password)"
  [WSO2_DB_PASSWORD]="$(password)"
  [WSO2_KEYSTORE_PASSWORD]="$(password)"
  [WSO2_ENCRYPTION_KEY]="$(openssl rand -hex 16)"
  [WSO2_AUTH_ENDPOINT_PASSWORD]="${endpoint_password}"
  [WSO2_AUTH_ENDPOINT_PASSWORD_SHA256]="$(printf '%s' "${endpoint_password}" | sha256sum | cut -d' ' -f1)"
  [DEMO_USER_PASSWORD]="$(password)"
  [AUDIT_HMAC_KEY]="$(openssl rand -base64 32)"
)

# Value of KEY in an env file (first match), without sourcing it.
env_get() { grep -E "^$1=" "$2" | head -n 1 | cut -d= -f2- || true; }

umask 077

if [ ! -f "${TARGET}" ]; then
  while IFS= read -r line; do
    key="${line%%=*}"
    if [[ "${line}" != \#* && "${line}" == *=* && -n "${secrets[${key}]+set}" ]]; then
      echo "${key}=${secrets[${key}]}"
    else
      echo "${line}"
    fi
  done < "${EXAMPLE}" > "${TARGET}"
  echo "Created .env with random secrets."
  exit 0
fi

chmod 600 "${TARGET}"

added=()
while IFS= read -r line; do
  [[ "${line}" == \#* || "${line}" != *=* ]] && continue
  key="${line%%=*}"
  if ! grep -qE "^${key}=" "${TARGET}"; then
    value="${secrets[${key}]-${line#*=}}"
    printf '%s=%s\n' "${key}" "${value}" >> "${TARGET}"
    added+=("${key}")
  fi
done < "${EXAMPLE}"
if [ "${#added[@]}" -gt 0 ]; then
  echo "Added to .env: ${added[*]}"
fi

# The SHA-256 must follow the endpoint password; a mismatch makes WSO2's login pages fail.
if [[ " ${added[*]} " == *" WSO2_AUTH_ENDPOINT_PASSWORD_SHA256 "* && " ${added[*]} " != *" WSO2_AUTH_ENDPOINT_PASSWORD "* ]]; then
  sha="$(printf '%s' "$(env_get WSO2_AUTH_ENDPOINT_PASSWORD "${TARGET}")" | sha256sum | cut -d' ' -f1)"
  sed -i "s|^WSO2_AUTH_ENDPOINT_PASSWORD_SHA256=.*|WSO2_AUTH_ENDPOINT_PASSWORD_SHA256=${sha}|" "${TARGET}"
fi

placeholders=()
for key in "${!secrets[@]}"; do
  example="$(env_get "${key}" "${EXAMPLE}")"
  if [ -n "${example}" ] && [ "$(env_get "${key}" "${TARGET}")" = "${example}" ]; then
    placeholders+=("${key}")
  fi
done
if [ "${#placeholders[@]}" -gt 0 ]; then
  echo "These .env values are still the published examples and must be rotated: ${placeholders[*]}" >&2
  echo "See 'Example secrets in .env' in docs/TROUBLESHOOTING.md." >&2
  exit 1
fi
echo ".env is complete (mode 600); existing values were left unchanged."
