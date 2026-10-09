#!/usr/bin/env bash
# Plays the guided tour of the demo end to end in headless Chromium against a running demo portal and WSO2:
# maker.hlb validates last month's MDA draft, checker.hlb submits it, the reviewer starts the review and generates an
# insight, approver.mfa approves it after the TOTP step, and the auditor verifies the audit chain (docs/DEMO.md).
#
#   scripts/demo-scenario.sh            after a reset
#   scripts/demo-scenario.sh --reset    signs in as admin.demo and presses Reset demo first (refused within the
#                                       cooldown, ten minutes after the last reset)
#
# It changes data: run it against a demo deployment (Demo:Enabled); the next reset undoes it. It reads the password
# and the authenticator keys from the public /demo page, as a visitor would, so it needs no secrets of its own.
# Override PORTAL_BASE, WSO2_BASE, WSO2_CA and APP_CA as for smoke-wso2.sh (APP_CA=system for the hosted demo). Needs
# Node with Playwright. TLS is always verified. Exit code 0 means the whole tour worked.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RESET_FIRST=false
for arg in "$@"; do
  case "${arg}" in
    --reset) RESET_FIRST=true ;;
    *) echo "Unknown option: ${arg}" >&2; exit 2 ;;
  esac
done
env_get() { [ -f "$1" ] && grep -E "^$2=" "$1" | tail -n 1 | cut -d= -f2- || true; }

WSO2_HOSTNAME="${WSO2_HOSTNAME:-$(env_get "${ROOT}/.env" WSO2_HOSTNAME)}"
WSO2_BASE="${WSO2_BASE:-https://${WSO2_HOSTNAME:-localhost}:9443}"
PORTAL_BASE="${PORTAL_BASE:-https://localhost:7101}"
WSO2_CA="${WSO2_CA:-${ROOT}/.certs/regreturns-dev-ca.crt}"

die() { echo "  FAIL  $*" >&2; exit 1; }

command -v node > /dev/null || die "Node with Playwright is needed"
[ -f "${WSO2_CA}" ] || die "WSO2 CA not found at ${WSO2_CA} (run scripts/dev-certs.sh or set WSO2_CA)"

WORK="$(mktemp -d)"
trap 'rm -rf -- "${WORK}"' EXIT
# The portal uses the ASP.NET Core development certificate locally; trust exactly that certificate.
if [ -z "${APP_CA:-}" ]; then
  APP_CA="${WORK}/aspnet-dev.pem"
  # Without a password option only the certificate is exported, never its private key.
  dotnet dev-certs https --export-path "${APP_CA}" --format PEM > /dev/null \
    || die "could not export the ASP.NET Core development certificate (dotnet dev-certs https)"
fi

echo "Guided tour against ${PORTAL_BASE} (WSO2 ${WSO2_BASE})"
NODE_PATH="${NODE_PATH:-$(npm root -g 2> /dev/null)}" PORTAL_BASE="${PORTAL_BASE}" WSO2_BASE="${WSO2_BASE}" \
  WSO2_CA="${WSO2_CA}" APP_CA="${APP_CA}" RESET_FIRST="${RESET_FIRST}" \
  node "${ROOT}/scripts/smoke/demo-scenario.cjs" || die "guided tour"
echo
echo "The guided tour worked end to end."
