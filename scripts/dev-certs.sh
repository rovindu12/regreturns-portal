#!/usr/bin/env bash
# Creates the local development CA and the keystores WSO2 Identity Server uses (ADR 0015).
#
#   .certs/regreturns-dev-ca.crt          CA certificate: the only root the apps trust for WSO2 (Wso2:TrustedCaPath)
#   .certs/regreturns-dev-ca.key          CA private key (stays on this machine)
#   .certs/wso2/regreturns-tls.p12        HTTPS certificate for localhost, iam.localhost and wso2, issued by the CA
#   .certs/wso2/regreturns-primary.p12    token-signing key, replacing WSO2's publicly known default key
#   .certs/wso2/regreturns-truststore.p12 WSO2's truststore with the CA and signing certificate added
#
# Re-running keeps the existing CA (so browsers that trust it keep working) and only creates what is missing.
# Pass --force to replace the WSO2 keystores. Requires openssl and Docker; reads passwords from .env.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${ROOT}/.certs"
WSO2_IMAGE="${WSO2_IMAGE:-wso2/wso2is:7.3.0}"
FORCE="${1:-}"

if [ -f "${ROOT}/.env" ]; then set -a; . "${ROOT}/.env"; set +a; fi
: "${WSO2_KEYSTORE_PASSWORD:?Set WSO2_KEYSTORE_PASSWORD in .env (see .env.example)}"

mkdir -p "${OUT}/wso2"
cd "${OUT}"

if [ ! -f regreturns-dev-ca.key ]; then
  echo "Creating the RegReturns development CA"
  openssl req -x509 -newkey rsa:3072 -sha256 -days 3650 -nodes \
    -keyout regreturns-dev-ca.key -out regreturns-dev-ca.crt \
    -subj "/O=RegReturns (development)/CN=RegReturns Dev CA" \
    -addext "basicConstraints=critical,CA:TRUE,pathlen:0" \
    -addext "keyUsage=critical,keyCertSign,cRLSign" 2> /dev/null
  chmod 0600 regreturns-dev-ca.key
fi

if [ -f wso2/regreturns-tls.p12 ] && [ "${FORCE}" != "--force" ]; then
  echo "WSO2 keystores already exist (use --force to replace them)"
  exit 0
fi

SANS="DNS:localhost,DNS:iam.localhost,DNS:wso2,IP:127.0.0.1"
if [ -n "${DOMAIN:-}" ]; then SANS="${SANS},DNS:iam.${DOMAIN}"; fi

TMP="$(mktemp -d)"
trap 'rm -rf "${TMP}"' EXIT

echo "Issuing the WSO2 HTTPS certificate (${SANS})"
openssl req -newkey rsa:2048 -nodes -keyout "${TMP}/tls.key" -out "${TMP}/tls.csr" \
  -subj "/O=RegReturns (development)/CN=localhost" 2> /dev/null
openssl x509 -req -in "${TMP}/tls.csr" -CA regreturns-dev-ca.crt -CAkey regreturns-dev-ca.key -CAcreateserial \
  -out "${TMP}/tls.crt" -days 825 -sha256 \
  -extfile <(printf "subjectAltName=%s\nkeyUsage=critical,digitalSignature,keyEncipherment\nextendedKeyUsage=serverAuth\nbasicConstraints=CA:FALSE\n" "${SANS}") 2> /dev/null
openssl pkcs12 -export -name regreturns-tls -in "${TMP}/tls.crt" -inkey "${TMP}/tls.key" \
  -certfile regreturns-dev-ca.crt -out wso2/regreturns-tls.p12 -passout env:WSO2_KEYSTORE_PASSWORD

echo "Creating the WSO2 token-signing key"
openssl req -x509 -newkey rsa:2048 -sha256 -days 1095 -nodes -keyout "${TMP}/signing.key" -out "${TMP}/signing.crt" \
  -subj "/O=RegReturns (development)/CN=RegReturns IAM token signing" 2> /dev/null
openssl pkcs12 -export -name regreturns-signing -in "${TMP}/signing.crt" -inkey "${TMP}/signing.key" \
  -out wso2/regreturns-primary.p12 -passout env:WSO2_KEYSTORE_PASSWORD
cp "${TMP}/signing.crt" wso2/regreturns-signing.crt
cp regreturns-dev-ca.crt wso2/regreturns-dev-ca.crt

echo "Building the WSO2 truststore"
# keytool runs inside the WSO2 image so the truststore starts from WSO2's own default entries.
docker run --rm --user root --entrypoint sh -e STOREPASS="${WSO2_KEYSTORE_PASSWORD}" -v "${OUT}/wso2:/out" "${WSO2_IMAGE}" -c '
  set -e
  cp "${WSO2_SERVER_HOME}/repository/resources/security/client-truststore.p12" /out/regreturns-truststore.p12
  keytool -storepasswd -keystore /out/regreturns-truststore.p12 -storetype PKCS12 -storepass wso2carbon -new "${STOREPASS}"
  keytool -importcert -noprompt -alias regreturns-dev-ca -file /out/regreturns-dev-ca.crt \
    -keystore /out/regreturns-truststore.p12 -storetype PKCS12 -storepass "${STOREPASS}"
  keytool -importcert -noprompt -alias regreturns-signing -file /out/regreturns-signing.crt \
    -keystore /out/regreturns-truststore.p12 -storetype PKCS12 -storepass "${STOREPASS}"
  chown '"$(id -u):$(id -g)"' /out/*'

# The WSO2 container runs as uid 802 and copies these files at start, so they must be readable.
# They hold development keys only; never reuse them outside a local machine.
chmod 0644 wso2/*.p12 wso2/*.crt
echo "Done. Trust ${OUT}/regreturns-dev-ca.crt in your browser to open https://localhost:9443 without warnings."
