#!/usr/bin/env bash
# Checks the edge proxy configuration (deploy/caddy/Caddyfile) in the pinned Caddy image, without the real apps.
#
#   1. Static: `caddy fmt` would not change the file, and `caddy validate` accepts it with production-like settings.
#   2. Functional: runs the real Caddyfile, unchanged, on a throwaway Docker network where stub upstreams answer as
#      web, api, seq and wso2 (wso2 over HTTPS, with a certificate from a throwaway CA mounted where the dev CA goes),
#      and proves with curl from this machine that
#        - each public name reaches its own upstream over HTTPS, HTTP redirects to HTTPS, and every answer (403s
#          included) carries the edge's HSTS header and no Server header;
#        - outside WSO2_ADMIN_ALLOWLIST the IAM name serves only the sign-in paths: the console, management APIs,
#          SCIM, My Account, account recovery, unknown paths and path tricks (..;/, %2e%2e, /../, //) answer 403, and
#          Seq answers 403;
#        - with the client's address on WSO2_ADMIN_ALLOWLIST the console, management APIs and Seq are reached;
#        - a WSO2 certificate from another CA is refused (502), so the upstream certificate is really verified;
#        - stdout carries only JSON access log entries, and tokens in URLs, Referer and Location are redacted.
#
# Three Caddy instances differ only in their environment: one whose allowlist holds documentation ranges (RFC 5737,
# RFC 3849) that no client ever has, one whose allowlist adds this machine's address as Caddy sees it (read from the
# first instance's access log, so the check works whatever source address Docker presents), and one with a foreign CA.
# The sites use *.localhost names, for which Caddy issues certificates from its own internal CA; curl verifies them
# against that CA's root, read from the container. Nothing is ever fetched with verification switched off.
#
# Needs Docker, curl, openssl and jq. Takes a few seconds once the image is pulled; prints a PASS or FAIL line per
# check and exits non-zero if any failed. Containers, network and temporary files are removed on exit.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CADDYFILE="${ROOT}/deploy/caddy/Caddyfile"
# The edge's image; keep in step with the production compose file (phase 11).
CADDY_IMAGE="caddy:2.10.2-alpine"
RUN_ID="regreturns-caddy-check-$$"
LABEL="org.regreturns.check=${RUN_ID}"
NETWORK="${RUN_ID}"
NOBODY="192.0.2.0/24 2001:db8::/32"

die() { echo "check-caddy: $*" >&2; exit 2; }
for tool in docker curl openssl jq; do command -v "${tool}" > /dev/null || die "${tool} is required"; done
docker info > /dev/null 2>&1 || die "Docker is not running"
[ -f "${CADDYFILE}" ] || die "${CADDYFILE} not found"

WORK="$(mktemp -d)"
# Containers run as root, or as a remapped user under rootless Docker: let them read the mounted test files.
chmod 0755 "${WORK}"

cleanup() {
  local containers
  containers="$(docker ps -aq --filter "label=${LABEL}" 2> /dev/null || true)"
  # shellcheck disable=SC2086 # one container id per word
  [ -z "${containers}" ] || docker rm -f ${containers} > /dev/null 2>&1 || true
  docker network rm "${NETWORK}" > /dev/null 2>&1 || true
  rm -rf "${WORK}"
}
trap cleanup EXIT

PASSES=0
FAILURES=0
pass() { echo "PASS  $1"; PASSES=$((PASSES + 1)); }
fail() { echo "FAIL  $1${2:+ (${2})}"; FAILURES=$((FAILURES + 1)); }

# Polls a command for up to 15 seconds.
wait_for() {
  for _ in $(seq 1 150); do
    if "$@" > /dev/null 2>&1; then return 0; fi
    sleep 0.1
  done
  return 1
}

docker image inspect "${CADDY_IMAGE}" > /dev/null 2>&1 || docker pull -q "${CADDY_IMAGE}" > /dev/null

# --- Throwaway certificates -------------------------------------------------------------------------------------------
# "trusted" stands in for the RegReturns dev CA and issues the stub WSO2 certificate (name wso2, like dev-certs.sh);
# "foreign" is mounted in its place for the refusal check.
new_ca() {
  openssl req -x509 -newkey ec -pkeyopt ec_paramgen_curve:P-256 -sha256 -days 1 -nodes \
    -keyout "${WORK}/$1-ca.key" -out "${WORK}/$1-ca.crt" -subj "/CN=RegReturns check $1 CA" \
    -addext "basicConstraints=critical,CA:TRUE,pathlen:0" -addext "keyUsage=critical,keyCertSign,cRLSign" 2> /dev/null
}
new_ca trusted
new_ca foreign
mkdir "${WORK}/wso2-tls"
openssl req -newkey ec -pkeyopt ec_paramgen_curve:P-256 -nodes -keyout "${WORK}/wso2-tls/wso2.key" \
  -out "${WORK}/wso2.csr" -subj "/CN=wso2" 2> /dev/null
cat > "${WORK}/wso2.ext" << 'EOF'
subjectAltName=DNS:wso2
keyUsage=critical,digitalSignature
extendedKeyUsage=serverAuth
basicConstraints=CA:FALSE
EOF
openssl x509 -req -in "${WORK}/wso2.csr" -CA "${WORK}/trusted-ca.crt" -CAkey "${WORK}/trusted-ca.key" \
  -CAcreateserial -out "${WORK}/wso2-tls/wso2.crt" -days 1 -sha256 -extfile "${WORK}/wso2.ext" 2> /dev/null
chmod 0755 "${WORK}/wso2-tls"
chmod 0644 "${WORK}/wso2-tls/"*

# --- 1. Static checks -------------------------------------------------------------------------------------------------
echo "Static checks (${CADDY_IMAGE})"
if docker run --rm --network none -v "${CADDYFILE}:/etc/caddy/Caddyfile:ro" "${CADDY_IMAGE}" \
  caddy fmt /etc/caddy/Caddyfile | diff -u "${CADDYFILE}" - > "${WORK}/fmt.diff"; then
  pass "caddy fmt leaves the Caddyfile unchanged"
else
  fail "caddy fmt leaves the Caddyfile unchanged" "run: caddy fmt --overwrite; diff follows"
  cat "${WORK}/fmt.diff"
fi

if docker run --rm --network none \
  -e PORTAL_HOST=regreturns.example.org -e API_HOST=api.example.org -e IAM_HOST=iam.example.org \
  -e SEQ_HOST=seq.example.org -e WSO2_ADMIN_ALLOWLIST="203.0.113.0/24 2001:db8:1::/48" \
  -v "${CADDYFILE}:/etc/caddy/Caddyfile:ro" -v "${WORK}/trusted-ca.crt:/etc/caddy/certs/regreturns-dev-ca.crt:ro" \
  "${CADDY_IMAGE}" caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile > "${WORK}/validate.log" 2>&1; then
  pass "caddy validate accepts the Caddyfile with production-like names"
else
  fail "caddy validate accepts the Caddyfile with production-like names"
  cat "${WORK}/validate.log"
fi

# --- 2. Functional checks: stub upstreams ----------------------------------------------------------------------------
# Each stub answers with its own name and the request line it received, sends a Server header and an HSTS header of
# its own (the edge must replace both), and, on /logout, a Location carrying the query (for the redaction check).
cat > "${WORK}/stub.Caddyfile" << 'EOF'
{
	admin off
}

:{$STUB_PORT} {
	header Server upstream
	header Strict-Transport-Security max-age=0
	header /logout Location "https://iam.localhost/oidc/logout?{query}"
	respond "upstream={$STUB_NAME} {method} {uri}"
}
EOF
cat > "${WORK}/stub-tls.Caddyfile" << 'EOF'
{
	admin off
}

https://:{$STUB_PORT} {
	tls /stub/wso2.crt /stub/wso2.key
	header Server upstream
	respond "upstream={$STUB_NAME} {method} {uri}"
}
EOF
chmod 0644 "${WORK}"/*.Caddyfile

docker network create --label "${LABEL}" "${NETWORK}" > /dev/null
start_stub() { # name port config
  docker run -d --name "${RUN_ID}-$1" --label "${LABEL}" --network "${NETWORK}" --network-alias "$1" \
    -e STUB_NAME="$1" -e STUB_PORT="$2" -v "${WORK}/$3:/etc/caddy/Caddyfile:ro" -v "${WORK}/wso2-tls:/stub:ro" \
    "${CADDY_IMAGE}" > /dev/null
}
start_stub web 8080 stub.Caddyfile
start_stub api 8080 stub.Caddyfile
start_stub seq 80 stub.Caddyfile
start_stub wso2 9443 stub-tls.Caddyfile

# --- Edge instances: the real Caddyfile, mounted read-only ------------------------------------------------------------
HOSTS=(regreturns.localhost api.localhost iam.localhost seq.localhost)
declare -A HTTPS_PORT HTTP_PORT
start_edge() { # instance allowlist ca-file
  docker run -d --name "${RUN_ID}-$1" --label "${LABEL}" --network "${NETWORK}" -p 127.0.0.1::80 -p 127.0.0.1::443 \
    -e PORTAL_HOST=regreturns.localhost -e API_HOST=api.localhost -e IAM_HOST=iam.localhost -e SEQ_HOST=seq.localhost \
    -e WSO2_ADMIN_ALLOWLIST="$2" \
    -v "${CADDYFILE}:/etc/caddy/Caddyfile:ro" -v "$3:/etc/caddy/certs/regreturns-dev-ca.crt:ro" \
    "${CADDY_IMAGE}" > /dev/null
}
edge_serves_all_hosts() { # instance
  local host
  for host in "${HOSTS[@]}"; do
    curl -s -o /dev/null --max-time 2 --cacert "${WORK}/$1-root.crt" \
      --resolve "${host}:${HTTPS_PORT[$1]}:127.0.0.1" "https://${host}:${HTTPS_PORT[$1]}/" || return 1
  done
}
wait_for_edge() { # instance
  local container="${RUN_ID}-$1" port
  if ! port="$(docker port "${container}" 443/tcp 2> /dev/null | head -n 1)" || [ -z "${port}" ]; then
    docker logs "${container}" >&2 || true
    die "Caddy ($1) did not start"
  fi
  HTTPS_PORT[$1]="${port##*:}"
  port="$(docker port "${container}" 80/tcp | head -n 1)"
  HTTP_PORT[$1]="${port##*:}"
  # The internal CA's root appears once Caddy has started; the site certificates follow moments later.
  if ! wait_for docker cp "${container}:/data/caddy/pki/authorities/local/root.crt" "${WORK}/$1-root.crt" \
    || ! wait_for edge_serves_all_hosts "$1"; then
    docker logs "${container}" >&2 || true
    die "Caddy ($1) did not serve all four sites within 15 seconds"
  fi
}
stub_ready() { docker logs "${RUN_ID}-$1" 2>&1 | grep -q 'serving initial configuration'; }
for stub in web api seq wso2; do wait_for stub_ready "${stub}" || die "stub upstream ${stub} did not start"; done

start_edge outside "${NOBODY}" "${WORK}/trusted-ca.crt"
start_edge foreign-ca "${NOBODY}" "${WORK}/foreign-ca.crt"
wait_for_edge outside
wait_for_edge foreign-ca

# This machine's address as Caddy sees it (Docker may present the network's gateway or another address).
CLIENT_IP="$(docker logs "${RUN_ID}-outside" 2> /dev/null \
  | jq -R -r 'fromjson? | select(.request.remote_ip) | .request.remote_ip' | tail -n 1)"
[ -n "${CLIENT_IP}" ] || die "could not read the client address from Caddy's access log (JSON on stdout)"
case "${CLIENT_IP}" in *:*) CLIENT_CIDR="${CLIENT_IP}/128" ;; *) CLIENT_CIDR="${CLIENT_IP}/32" ;; esac
start_edge allowlisted "${NOBODY} ${CLIENT_CIDR}" "${WORK}/trusted-ca.crt"
wait_for_edge allowlisted

# --- Requests ---------------------------------------------------------------------------------------------------------
# expect INSTANCE HOST PATH STATUS [UPSTREAM] [curl options...]: the answer has STATUS, the edge's headers, and, when
# UPSTREAM is given, comes from that upstream with the request line unchanged.
expect() {
  local instance="$1" host="$2" path="$3" status="$4" upstream="${5:-}" got label hsts
  shift "$(($# < 5 ? $# : 5))"
  label="[${instance}] https://${host}${path} -> ${status}${upstream:+ from ${upstream}}"
  rm -f "${WORK}/headers" "${WORK}/body"
  got="$(curl -sS --path-as-is --max-time 5 --cacert "${WORK}/${instance}-root.crt" \
    --resolve "${host}:${HTTPS_PORT[$instance]}:127.0.0.1" -D "${WORK}/headers" -o "${WORK}/body" -w '%{http_code}' \
    "$@" "https://${host}:${HTTPS_PORT[$instance]}${path}" 2> "${WORK}/curl.err")" \
    || got="curl error: $(cat "${WORK}/curl.err")"
  tr -d '\r' < "${WORK}/headers" > "${WORK}/headers.txt" 2> /dev/null || : > "${WORK}/headers.txt"
  hsts="$(grep -i '^strict-transport-security:' "${WORK}/headers.txt" || true)"
  if [ "${got}" != "${status}" ]; then
    fail "${label}" "got ${got}"
  elif [ -n "${upstream}" ] && [ "$(cat "${WORK}/body")" != "upstream=${upstream} GET ${path}" ]; then
    fail "${label}" "body: $(head -c 200 "${WORK}/body")"
  elif [ "${hsts,,}" != "strict-transport-security: max-age=31536000; includesubdomains" ]; then
    # Exactly one header with the edge's value: the upstream's own must be replaced, not repeated.
    fail "${label}" "HSTS: ${hsts:-missing}"
  elif grep -qi '^server:' "${WORK}/headers.txt"; then
    fail "${label}" "Server header present"
  else
    pass "${label}"
  fi
}

echo "Each public name reaches its own upstream (outside the allowlist)"
expect outside regreturns.localhost /bank/returns 200 web
expect outside api.localhost /v1/return-types 200 api
expect outside iam.localhost /oauth2/token/.well-known/openid-configuration 200 wso2
expect outside iam.localhost '/oauth2/authorize?response_type=code&client_id=portal' 200 wso2
expect outside iam.localhost /oauth2/token 200 wso2
expect outside iam.localhost /oauth2/jwks 200 wso2
expect outside iam.localhost /oidc/logout 200 wso2
expect outside iam.localhost /oidc/checksession 200 wso2
expect outside iam.localhost /authenticationendpoint/login.do 200 wso2
expect outside iam.localhost /commonauth 200 wso2
expect outside iam.localhost /logincontext 200 wso2

label="[outside] http://regreturns.localhost/reports?x=1 -> 308 to https"
got="$(curl -sS --max-time 5 -o /dev/null -w '%{http_code} %{redirect_url}' \
  --resolve "regreturns.localhost:${HTTP_PORT[outside]}:127.0.0.1" \
  "http://regreturns.localhost:${HTTP_PORT[outside]}/reports?x=1" 2>&1)" || true
if [ "${got}" = "308 https://regreturns.localhost/reports?x=1" ]; then
  pass "${label}"
else
  fail "${label}" "got ${got}"
fi

echo "Admin surfaces are refused outside the allowlist"
for path in /console /console/ /carbon/admin/login.jsp /api/server/v1/applications /api/users/v1/me /scim2/Users \
  /scim2/Me /myaccount /accounts /accountrecoveryendpoint/recoverpassword.do / /unknown \
  '/oauth2/..;/console' '/oauth2/..%3B/console' '/commonauth/..;/carbon/' '/oauth2/%2e%2e/console' \
  '/oauth2/../console' '//console' '/oauth2;x/../api/server/v1/applications'; do
  expect outside iam.localhost "${path}" 403
done
expect outside seq.localhost / 403
expect outside seq.localhost /api/events 403

echo "Admin surfaces are reached from an allowlisted address (${CLIENT_CIDR})"
expect allowlisted iam.localhost /console 200 wso2
expect allowlisted iam.localhost /api/server/v1/applications 200 wso2
expect allowlisted iam.localhost /scim2/Users 200 wso2
expect allowlisted iam.localhost /carbon/admin/login.jsp 200 wso2
expect allowlisted iam.localhost /oauth2/jwks 200 wso2
expect allowlisted seq.localhost / 200 seq
expect allowlisted seq.localhost /api/events 200 seq
expect allowlisted regreturns.localhost / 200 web

echo "WSO2's certificate is verified against the mounted CA only"
expect foreign-ca iam.localhost /oauth2/jwks 502
expect foreign-ca regreturns.localhost / 200 web

echo "Access logs"
MARKER="check-token-$(openssl rand -hex 8)"
expect outside regreturns.localhost "/logout?id_token_hint=${MARKER}&state=s1" 200 web \
  -e "https://iam.localhost/oidc/logout?id_token_hint=${MARKER}"
expect outside iam.localhost "/oauth2/authorize?code=${MARKER}&client_secret=${MARKER}" 200 wso2
docker logs "${RUN_ID}-outside" > "${WORK}/stdout.log" 2> "${WORK}/stderr.log"
label="[outside] stdout carries only JSON access log entries"
if [ -s "${WORK}/stdout.log" ] \
  && jq -e -s 'all(.[]; type == "object" and ((.logger // "") | startswith("http.log.access")))' \
    "${WORK}/stdout.log" > /dev/null 2>&1; then
  pass "${label}"
else
  fail "${label}" "first line: $(head -c 200 "${WORK}/stdout.log")"
fi
label="[outside] tokens in URLs, Referer and Location are redacted in the access log"
redacted="$(jq -r 'select(.request.uri | startswith("/logout"))
  | [.request.uri, .request.headers.Referer[0], .resp_headers.Location[0]] | join(" ")' \
  "${WORK}/stdout.log" 2> /dev/null || true)"
if grep -q -- "${MARKER}" "${WORK}/stdout.log" "${WORK}/stderr.log"; then
  fail "${label}" "a token value reached the logs"
elif [ "$(grep -o 'id_token_hint=REDACTED' <<< "${redacted}" | wc -l)" -ne 3 ]; then
  fail "${label}" "logged: ${redacted:-nothing}"
else
  pass "${label}"
fi

echo
if [ "${FAILURES}" -eq 0 ]; then
  echo "All ${PASSES} Caddy checks passed."
else
  echo "${FAILURES} of $((PASSES + FAILURES)) Caddy checks failed."
  exit 1
fi
