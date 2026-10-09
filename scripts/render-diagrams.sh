#!/usr/bin/env bash
# Renders the Mermaid diagrams in docs/diagrams to static SVG for the portal (ADR 0031): architecture.mmd becomes
# src/RegReturns.Web/wwwroot/img/architecture.svg, which the landing page shows as an image. Run it after changing a
# .mmd file and commit both; the portal never runs Mermaid itself.
#
#   scripts/render-diagrams.sh
#
# Needs Node: npx fetches the Mermaid CLI at the version pinned below. Mermaid draws with a headless Chromium; set
# CHROMIUM to use an installed one (for example /opt/pw-browsers/chromium) instead of Puppeteer's download.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MERMAID_CLI_VERSION=11.4.2
CONFIG="${ROOT}/docs/diagrams/mermaid.json"

command -v npx > /dev/null || { echo "Node (npx) is needed" >&2; exit 1; }

WORK="$(mktemp -d)"
trap 'rm -rf -- "${WORK}"' EXIT
puppeteer=()
if [ -n "${CHROMIUM:-}" ]; then
  sandbox='[]'
  # Chromium refuses to run as root with its sandbox (as in a container).
  [ "$(id -u)" -eq 0 ] && sandbox='["--no-sandbox"]'
  printf '{ "executablePath": "%s", "args": %s }\n' "${CHROMIUM}" "${sandbox}" > "${WORK}/puppeteer.json"
  puppeteer=(-p "${WORK}/puppeteer.json")
  export PUPPETEER_SKIP_DOWNLOAD=true
fi

render() {
  local source="$1" target="$2"
  npx --yes "@mermaid-js/mermaid-cli@${MERMAID_CLI_VERSION}" -q -i "${source}" -o "${target}" -c "${CONFIG}" \
    -b transparent "${puppeteer[@]}"
  # HTML labels would need foreignObject, which an <img> does not render reliably; the config turns them off.
  if grep -q foreignObject "${target}"; then
    echo "${target} contains HTML labels (foreignObject); check htmlLabels in ${CONFIG}" >&2
    exit 1
  fi
  echo "rendered ${source#"${ROOT}/"} -> ${target#"${ROOT}/"}"
}

render "${ROOT}/docs/diagrams/architecture.mmd" "${ROOT}/src/RegReturns.Web/wwwroot/img/architecture.svg"
