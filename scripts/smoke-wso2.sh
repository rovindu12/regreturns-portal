#!/usr/bin/env bash
# End-to-end smoke test of the identity setup against a running WSO2, API and (optionally) portal.
#
#   scripts/smoke-wso2.sh              WSO2 discovery and JWKS, a bank's client-credentials token, the API's
#                                      institution scoping and audience check, and a headless maker login + logout
#   scripts/smoke-wso2.sh --browser    also signs in through the portal in headless Chromium (Playwright)
#
# Reads .env and .env.generated (IamBootstrap). Override with WSO2_BASE, API_BASE, PORTAL_BASE, WSO2_CA and APP_CA
# (APP_CA=system uses the system trust store, for the hosted demo). Needs bash, curl, openssl, python3; --browser also
# needs Node with Playwright. TLS is always verified: WSO2 against the dev CA, the apps against the ASP.NET Core
# development certificate. Secrets are never printed. Exit code 0 means every check passed.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BROWSER=false
for arg in "$@"; do
  case "${arg}" in
    --browser) BROWSER=true ;;
    *) echo "Unknown option: ${arg}" >&2; exit 2 ;;
  esac
done

env_get() { [ -f "$1" ] && grep -E "^$2=" "$1" | tail -n 1 | cut -d= -f2- || true; }
setting() { local value="${!1:-}"; [ -n "${value}" ] || value="$(env_get "${ROOT}/.env.generated" "$1")"; \
  [ -n "${value}" ] || value="$(env_get "${ROOT}/.env" "$1")"; printf '%s' "${value}"; }

WSO2_HOSTNAME="$(setting WSO2_HOSTNAME)"
WSO2_BASE="${WSO2_BASE:-https://${WSO2_HOSTNAME:-localhost}:9443}"
API_BASE="${API_BASE:-https://localhost:7201}"
PORTAL_BASE="${PORTAL_BASE:-https://localhost:7101}"
WSO2_CA="${WSO2_CA:-${ROOT}/.certs/regreturns-dev-ca.crt}"
API_AUDIENCE="https://api.regreturns"
OWN_BANK=HLB
OTHER_BANK=CCB
MAKER="maker.$(echo "${OWN_BANK}" | tr '[:upper:]' '[:lower:]')"

BANK_CLIENT_ID="$(setting "BANK_${OWN_BANK}_CLIENT_ID")"
BANK_CLIENT_SECRET="$(setting "BANK_${OWN_BANK}_CLIENT_SECRET")"
PORTAL_CLIENT_ID="$(setting Oidc__ClientId)"
PORTAL_CLIENT_SECRET="$(setting Oidc__ClientSecret)"
DEMO_PASSWORD="$(setting DEMO_USER_PASSWORD)"

WORK="$(mktemp -d)"
trap 'rm -rf -- "${WORK}"' EXIT
JAR="${WORK}/cookies"
passed=0

ok() { passed=$((passed + 1)); echo "  ok    $*"; }
die() { echo "  FAIL  $*" >&2; echo "${passed} check(s) passed before the failure." >&2; exit 1; }
section() { echo; echo "$*"; }

[ -f "${WSO2_CA}" ] || die "WSO2 CA not found at ${WSO2_CA} (run scripts/dev-certs.sh or set WSO2_CA)"
for name in BANK_CLIENT_ID BANK_CLIENT_SECRET PORTAL_CLIENT_ID PORTAL_CLIENT_SECRET DEMO_PASSWORD; do
  [ -n "${!name}" ] || die "${name} is not set: run IamBootstrap apply (it writes .env.generated)"
done

# The apps use the ASP.NET Core development certificate locally; trust exactly that certificate.
if [ "${APP_CA:-}" = "system" ]; then
  app_tls=()
else
  if [ -z "${APP_CA:-}" ]; then
    APP_CA="${WORK}/aspnet-dev.pem"
    dotnet dev-certs https --export-path "${APP_CA}" --format PEM --no-password > /dev/null \
      || die "could not export the ASP.NET Core development certificate (dotnet dev-certs https)"
  fi
  app_tls=(--cacert "${APP_CA}")
fi

wso2() { curl -sS --cacert "${WSO2_CA}" -c "${JAR}" -b "${JAR}" "$@"; }
app() { curl -sS "${app_tls[@]}" "$@"; }
redirect_of() { wso2 -o /dev/null -w '%{redirect_url}' "$@"; }
query_param() { python3 -c 'import sys,urllib.parse as u; print(u.parse_qs(u.urlparse(sys.argv[1]).query).get(sys.argv[2],[""])[0])' "$1" "$2"; }
url_encode() { python3 -c 'import sys,urllib.parse as u; print(u.quote(sys.argv[1],safe=""))' "$1"; }
json_get() { python3 -c 'import json,sys; v=json.load(sys.stdin)
for k in sys.argv[1].split("."): v=v.get(k) if isinstance(v,dict) else None
print("" if v is None else v)' "$1"; }

# jwt_check <token> <checks...>: verifies the RS256 signature against the JWKS, then evaluates each check, a Python
# expression over the header `h` and the payload `p` (helpers: `has(claim, value)` treats string or list alike).
jwt_check() {
  TOKEN="$1" JWKS_FILE="${WORK}/jwks.json" python3 - "${@:2}" <<'PY'
import base64, json, os, subprocess, sys, tempfile
def b64d(s): return base64.urlsafe_b64decode(s + '=' * (-len(s) % 4))
def der_len(n): return bytes([n]) if n < 128 else (lambda b: bytes([0x80 | len(b)]) + b)(n.to_bytes((n.bit_length() + 7) // 8, 'big'))
def der(tag, body): return bytes([tag]) + der_len(len(body)) + body
def der_int(b): return der(0x02, (b'\x00' + b) if b[0] & 0x80 else b)
def pem(jwk):
    rsa = der(0x30, der_int(b64d(jwk['n'])) + der_int(b64d(jwk['e'])))
    spki = der(0x30, der(0x30, bytes.fromhex('06092a864886f70d0101010500')) + der(0x03, b'\x00' + rsa))
    return '-----BEGIN PUBLIC KEY-----\n' + base64.encodebytes(spki).decode() + '-----END PUBLIC KEY-----\n'
token = os.environ['TOKEN']
h64, p64, s64 = token.split('.')
h, p = json.loads(b64d(h64)), json.loads(b64d(p64))
keys = json.load(open(os.environ['JWKS_FILE']))['keys']
key = next((k for k in keys if k.get('kid') == h.get('kid')), None)
if key is None: sys.exit(f"signing key {h.get('kid')} is not in the JWKS")
if h.get('alg') != 'RS256': sys.exit(f"unexpected alg {h.get('alg')}")
with tempfile.TemporaryDirectory() as d:
    open(f'{d}/k.pem', 'w').write(pem(key)); open(f'{d}/s', 'wb').write(b64d(s64)); open(f'{d}/m', 'w').write(f'{h64}.{p64}')
    r = subprocess.run(['openssl', 'dgst', '-sha256', '-verify', f'{d}/k.pem', '-signature', f'{d}/s', f'{d}/m'], capture_output=True)
if r.returncode != 0: sys.exit('signature does not verify')
def has(claim, value):
    v = p.get(claim, [])
    return value in (v.split() if claim == 'scope' and isinstance(v, str) else ([v] if isinstance(v, str) else v))
for check in sys.argv[1:]:
    if not eval(check, {'h': h, 'p': p, 'has': has}): sys.exit(f'check failed: {check}')
PY
}

section "WSO2 at ${WSO2_BASE}"
discovery="$(wso2 --fail "${WSO2_BASE}/oauth2/token/.well-known/openid-configuration")" || die "discovery document unavailable"
[ "$(json_get issuer <<< "${discovery}")" = "${WSO2_BASE}/oauth2/token" ] || die "issuer is not ${WSO2_BASE}/oauth2/token"
python3 -c 'import json,sys; d=json.load(sys.stdin); sys.exit(0 if "S256" in d.get("code_challenge_methods_supported",[]) and d.get("end_session_endpoint") else 1)' \
  <<< "${discovery}" || die "discovery lacks PKCE S256 or an end-session endpoint"
ok "discovery: issuer, PKCE S256 and end-session endpoint"
wso2 --fail -o "${WORK}/jwks.json" "${WSO2_BASE}/oauth2/jwks" || die "JWKS unavailable"
python3 -c 'import json,sys; k=json.load(open(sys.argv[1]))["keys"]; sys.exit(0 if any(x.get("kty")=="RSA" for x in k) else 1)' \
  "${WORK}/jwks.json" || die "JWKS has no RSA key"
ok "JWKS publishes an RSA signing key"

section "Bank client ${OWN_BANK} (client credentials)"
token_response="$(curl -sS --cacert "${WSO2_CA}" -u "${BANK_CLIENT_ID}:${BANK_CLIENT_SECRET}" \
  --data-urlencode grant_type=client_credentials --data-urlencode "scope=returns:read reference:read" \
  "${WSO2_BASE}/oauth2/token")"
bank_token="$(json_get access_token <<< "${token_response}")"
[ -n "${bank_token}" ] || die "no token: $(json_get error <<< "${token_response}") $(json_get error_description <<< "${token_response}")"
jwt_check "${bank_token}" \
  "h.get('typ') == 'at+jwt'" \
  "p['iss'] == '${WSO2_BASE}/oauth2/token'" \
  "has('aud', '${API_AUDIENCE}')" \
  "p.get('aut') == 'APPLICATION'" \
  "p.get('azp') == '${BANK_CLIENT_ID}'" \
  "has('scope', 'reference:read') and has('scope', 'returns:read')" \
  "p['exp'] - p['iat'] <= 300" || die "bank access token"
ok "signed at+jwt for the API audience, APPLICATION type, requested scopes, at most 5 minutes"

section "API at ${API_BASE}"
status() { app -o "${WORK}/body" -w '%{http_code}' "$@" || echo 000; }
code="$(status "${API_BASE}/health/live")"
[ "${code}" = 200 ] || die "API not reachable (HTTP ${code}); start it with: dotnet run --project src/RegReturns.Api"
code="$(status -H "Authorization: Bearer ${bank_token}" "${API_BASE}/v1/me")"
[ "${code}" = 200 ] || die "GET /v1/me with the bank token returned ${code}"
[ "$(json_get institution.code < "${WORK}/body")" = "${OWN_BANK}" ] || die "GET /v1/me did not name ${OWN_BANK}"
ok "GET /v1/me identifies the caller as ${OWN_BANK}"
code="$(status -H "Authorization: Bearer ${bank_token}" "${API_BASE}/v1/institutions/${OWN_BANK}")"
[ "${code}" = 200 ] || die "own institution returned ${code}"
ok "GET /v1/institutions/${OWN_BANK} returns 200"
code="$(status -H "Authorization: Bearer ${bank_token}" "${API_BASE}/v1/institutions/${OTHER_BANK}")"
[ "${code}" = 404 ] || die "another bank's institution returned ${code}, expected 404"
ok "GET /v1/institutions/${OTHER_BANK} returns 404 (no cross-bank access)"
code="$(status "${API_BASE}/v1/me")"
[ "${code}" = 401 ] || die "no token returned ${code}, expected 401"
ok "no token: 401"
tampered="${bank_token%.*}.$(printf 'forged' | openssl base64 -A | tr '+/' '-_' | tr -d '=')"
code="$(status -H "Authorization: Bearer ${tampered}" "${API_BASE}/v1/me")"
[ "${code}" = 401 ] || die "forged signature returned ${code}, expected 401"
ok "forged signature: 401"

section "Portal login as ${MAKER} (authorization code + PKCE, headless)"
redirect_uri="${PORTAL_BASE}/signin-oidc"
post_logout_uri="${PORTAL_BASE}/signout-callback-oidc"
verifier="$(openssl rand -hex 32)"
challenge="$(printf %s "${verifier}" | openssl dgst -sha256 -binary | openssl base64 -A | tr '+/' '-_' | tr -d '=')"
state="$(openssl rand -hex 8)"; nonce="$(openssl rand -hex 8)"
authorize="${WSO2_BASE}/oauth2/authorize?response_type=code&client_id=$(url_encode "${PORTAL_CLIENT_ID}")"
authorize+="&redirect_uri=$(url_encode "${redirect_uri}")&scope=$(url_encode "openid profile email roles institution")"
authorize+="&state=${state}&nonce=${nonce}&code_challenge=${challenge}&code_challenge_method=S256"
location="$(redirect_of "${authorize}")"
[[ "${location}" == *login.do* ]] || die "authorize did not show the login page: ${location%%\?*}"
session_key="$(query_param "${location}" sessionDataKey)"
location="$(redirect_of --data-urlencode "username=${MAKER}" --data-urlencode "password=${DEMO_PASSWORD}" \
  --data-urlencode "sessionDataKey=${session_key}" "${WSO2_BASE}/commonauth")"
[[ "${location}" != *authFailure=true* && "${location}" != *login.do* ]] || die "WSO2 rejected the demo password for ${MAKER}"
location="$(redirect_of "${location}")"
[[ "${location}" != *consent* ]] || die "WSO2 asked for consent; the portal app should skip it (re-run IamBootstrap apply)"
[[ "${location}" == "${redirect_uri}"* ]] || die "login did not return to the portal callback: ${location%%\?*}"
[ "$(query_param "${location}" state)" = "${state}" ] || die "state mismatch"
auth_code="$(query_param "${location}" code)"
[ -n "${auth_code}" ] || die "no authorization code: $(query_param "${location}" error)"
ok "login form accepted and code returned to ${redirect_uri}"
tokens="$(curl -sS --cacert "${WSO2_CA}" -u "${PORTAL_CLIENT_ID}:${PORTAL_CLIENT_SECRET}" \
  --data-urlencode grant_type=authorization_code --data-urlencode "code=${auth_code}" \
  --data-urlencode "redirect_uri=${redirect_uri}" --data-urlencode "code_verifier=${verifier}" "${WSO2_BASE}/oauth2/token")"
id_token="$(json_get id_token <<< "${tokens}")"
user_token="$(json_get access_token <<< "${tokens}")"
[ -n "${id_token}" ] || die "token exchange failed: $(json_get error <<< "${tokens}")"
jwt_check "${id_token}" \
  "p.get('nonce') == '${nonce}'" \
  "has('aud', '${PORTAL_CLIENT_ID}')" \
  "has('roles', 'bank_maker')" \
  "p.get('institution_id') == '${OWN_BANK}'" \
  "p.get('username', '${MAKER}') == '${MAKER}'" \
  "has('amr', 'BasicAuthenticator')" \
  "bool(p.get('sid'))" || die "ID token claims"
ok "ID token: nonce, bank_maker role, institution_id ${OWN_BANK}, password authentication, session id"
code="$(status -H "Authorization: Bearer ${user_token}" "${API_BASE}/v1/me")"
[ "${code}" = 401 ] || die "the portal's access token (wrong audience) returned ${code} from the API, expected 401"
ok "API rejects the portal's access token (wrong audience): 401"
location="$(redirect_of -G "${WSO2_BASE}/oidc/logout" --data-urlencode "id_token_hint=${id_token}" \
  --data-urlencode "post_logout_redirect_uri=${post_logout_uri}" --data-urlencode "state=${state}")"
[[ "${location}" == "${post_logout_uri}"* ]] || die "logout did not return to ${post_logout_uri}: ${location%%\?*}"
location="$(redirect_of "${authorize}&prompt=none")"
[ "$(query_param "${location}" error)" = login_required ] || die "the WSO2 session survived logout"
ok "logout returns to the portal and ends the WSO2 session"

if [ "${BROWSER}" = true ]; then
  section "Portal at ${PORTAL_BASE} (headless Chromium)"
  command -v node > /dev/null || die "--browser needs Node and Playwright"
  NODE_PATH="${NODE_PATH:-$(npm root -g 2> /dev/null)}" PORTAL_BASE="${PORTAL_BASE}" WSO2_BASE="${WSO2_BASE}" \
    WSO2_CA="${WSO2_CA}" APP_CA="${APP_CA:-system}" LOGIN_USER="${MAKER}" LOGIN_PASSWORD="${DEMO_PASSWORD}" \
    EXPECT_TEXT="${OWN_BANK}" node "${ROOT}/scripts/smoke/portal-login.cjs" || die "browser sign-in"
  ok "browser sign-in as ${MAKER} lands on the bank dashboard, sign-out returns to the landing page"
fi

echo
echo "All ${passed} checks passed."
