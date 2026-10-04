#!/usr/bin/env bash
# Copies local secrets into `dotnet user-secrets` for every runnable project, so `dotnet run` needs no
# environment variables. Reads .env (scripts/init-env.sh) and, when present, .env.generated (written by
# IamBootstrap). Safe to re-run after either file changes. Secrets travel over stdin and are never printed.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="${ROOT}/.env"
GENERATED_FILE="${ROOT}/.env.generated"

[ -f "${ENV_FILE}" ] || { echo "No .env: run scripts/init-env.sh first" >&2; exit 1; }
command -v jq > /dev/null || { echo "jq is required" >&2; exit 1; }

# Value of KEY in an env file (last one wins), without sourcing the file.
env_get() { [ -f "$1" ] && grep -E "^$2=" "$1" | tail -n 1 | cut -d= -f2- || true; }

# Clones made before the audit key existed get one appended; Web and Api must share it to verify the chain.
if [ -z "$(env_get "${ENV_FILE}" AUDIT_HMAC_KEY)" ]; then
  printf '\n# HMAC key for the hash-chained audit trail (ADR 0016), base64 of 32 random bytes.\nAUDIT_HMAC_KEY=%s\n' \
    "$(openssl rand -base64 32)" >> "${ENV_FILE}"
  echo "Added AUDIT_HMAC_KEY to .env"
fi

sa_password="$(env_get "${ENV_FILE}" MSSQL_SA_PASSWORD)"
[ -n "${sa_password}" ] || { echo "MSSQL_SA_PASSWORD is empty in .env" >&2; exit 1; }
connection="Server=localhost,1433;Database=RegReturns;User Id=sa;Password=${sa_password};TrustServerCertificate=True"
audit_key="$(env_get "${ENV_FILE}" AUDIT_HMAC_KEY)"
portal_secret="$(env_get "${GENERATED_FILE}" Oidc__ClientSecret)"

# set_secrets <project> <jq filter>: merges the JSON object into the project's user-secrets.
set_secrets() {
  jq -n --arg cs "${connection}" --arg audit "${audit_key}" --arg portal "${portal_secret}" "$2" |
    dotnet user-secrets set --project "${ROOT}/$1" > /dev/null
  echo "Updated user-secrets for $1"
}

set_secrets tools/RegReturns.Migrator '{"ConnectionStrings:RegReturns": $cs}'
set_secrets tools/RegReturns.IamBootstrap '{"ConnectionStrings:RegReturns": $cs}'
set_secrets src/RegReturns.Api '{"ConnectionStrings:RegReturns": $cs, "Audit:HmacKey": $audit}'
set_secrets src/RegReturns.Web \
  '{"ConnectionStrings:RegReturns": $cs, "Audit:HmacKey": $audit} + (if $portal == "" then {} else {"Oidc:ClientSecret": $portal} end)'

if [ -z "${portal_secret}" ]; then
  echo "No portal client secret yet: run IamBootstrap (apply), then this script again."
fi
