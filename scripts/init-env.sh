#!/usr/bin/env bash
# Creates .env from .env.example with random secrets, so a fresh clone never runs on example passwords.
# Leaves an existing .env alone unless --force is given.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TARGET="${ROOT}/.env"
if [ -f "${TARGET}" ] && [ "${1:-}" != "--force" ]; then
  echo ".env already exists (use --force to regenerate it)"
  exit 0
fi

# Strong password that meets SQL Server's policy and contains no quotes or shell metacharacters.
password() { printf '%s_Aa1' "$(openssl rand -base64 24 | tr -dc 'A-Za-z0-9' | head -c 24)"; }

endpoint_password="$(password)"
declare -A values=(
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

umask 077
while IFS= read -r line; do
  key="${line%%=*}"
  if [[ "${line}" != \#* && "${line}" == *=* && -n "${values[${key}]+set}" ]]; then
    echo "${key}=${values[${key}]}"
  else
    echo "${line}"
  fi
done < "${ROOT}/.env.example" > "${TARGET}"
echo "Created .env with random secrets."
