#!/usr/bin/env bash
# Copies local secrets into `dotnet user-secrets` for every runnable project, so `dotnet run` needs no
# environment variables. Reads .env (scripts/init-env.sh) and, when present, .env.generated (written by
# IamBootstrap). Safe to re-run after either file changes. Secrets travel over stdin and are never printed.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="${ROOT}/.env"
GENERATED_FILE="${ROOT}/.env.generated"

command -v jq > /dev/null || { echo "jq is required" >&2; exit 1; }

# Creates or completes .env (owner-only, missing keys added) and stops if a secret is still an example value.
"${ROOT}/scripts/init-env.sh"

# Value of KEY in an env file (last one wins), without sourcing the file.
env_get() { [ -f "$1" ] && grep -E "^$2=" "$1" | tail -n 1 | cut -d= -f2- || true; }

sa_password="$(env_get "${ENV_FILE}" MSSQL_SA_PASSWORD)"
[ -n "${sa_password}" ] || { echo "MSSQL_SA_PASSWORD is empty in .env" >&2; exit 1; }
# TrustServerCertificate: the SQL Server container's certificate is self-signed until the hardening phase gives it
# one from the development CA (plan §10, phase 10). The port is published on 127.0.0.1 only.
connection="Server=localhost,1433;Database=RegReturns;User Id=sa;Password=${sa_password};TrustServerCertificate=True"
audit_key="$(env_get "${ENV_FILE}" AUDIT_HMAC_KEY)"
portal_secret="$(env_get "${GENERATED_FILE}" Oidc__ClientSecret)"

# set_secrets <project> <jq filter>: merges the JSON object into the project's user-secrets. jq reads the values
# from its environment ($ENV), not from --arg, so they never appear in the process list.
set_secrets() {
  RR_CS="${connection}" RR_AUDIT="${audit_key}" RR_PORTAL="${portal_secret}" jq -n "$2" |
    dotnet user-secrets set --project "${ROOT}/$1" > /dev/null
  echo "Updated user-secrets for $1"
}

set_secrets tools/RegReturns.Migrator '{"ConnectionStrings:RegReturns": $ENV.RR_CS}'
set_secrets tools/RegReturns.IamBootstrap '{"ConnectionStrings:RegReturns": $ENV.RR_CS}'
set_secrets src/RegReturns.Api '{"ConnectionStrings:RegReturns": $ENV.RR_CS, "Audit:HmacKey": $ENV.RR_AUDIT}'
set_secrets src/RegReturns.Web \
  '{"ConnectionStrings:RegReturns": $ENV.RR_CS, "Audit:HmacKey": $ENV.RR_AUDIT}
   + (if $ENV.RR_PORTAL == "" then {} else {"Oidc:ClientSecret": $ENV.RR_PORTAL} end)'

if [ -z "${portal_secret}" ]; then
  echo "No portal client secret yet: run IamBootstrap (apply), then this script again."
fi
