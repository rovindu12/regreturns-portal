#!/usr/bin/env bash
# Creates the local development CA, SQL Server's certificate (ADR 0033) and the keystores WSO2 Identity Server uses
# (ADR 0015).
#
#   .certs/regreturns-dev-ca.crt          CA certificate: the only root the apps trust for WSO2 (Wso2:TrustedCaPath)
#   .certs/regreturns-dev-ca.key          CA private key (stays on this machine)
#   .certs/sqlserver/mssql.crt            SQL Server's TLS certificate for sqlserver, localhost and 127.0.0.1, issued by
#                                         the CA; the apps pin it (ServerCertificate), sqlcmd checks it with -J
#   .certs/sqlserver/mssql.key            its private key (mode 600; the sqlserver-tls job installs it for SQL Server)
#   .certs/wso2/regreturns-tls.p12        HTTPS certificate for localhost, iam.localhost and wso2, issued by the CA
#   .certs/wso2/regreturns-primary.p12    token-signing key, replacing WSO2's publicly known default key
#   .certs/wso2/regreturns-truststore.p12 WSO2's truststore: its public roots plus the CA and signing certificate,
#                                         without WSO2's default "wso2carbon" certificate (its private key is public)
#
# Re-running keeps the existing CA (so browsers that trust it keep working) and only creates what is missing. The
# keystores are built in a staging folder and moved into place together, so an interrupted run leaves nothing half
# made. Pass --force to replace the WSO2 keystores and SQL Server's certificate (WSO2 and SQL Server then need a
# restart, tokens WSO2 signed stop validating, and scripts/dev-secrets.sh has nothing to change: it names the file).
# Requires openssl and Docker; reads the keystore password from .env and never passes it on a command line.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${ROOT}/.certs"
WSO2_IMAGE="${WSO2_IMAGE:-wso2/wso2is:7.3.0}"
FORCE="${1:-}"

# Value of KEY from the environment or .env, without sourcing the file.
setting() { if [ -n "${!1:-}" ]; then printf '%s' "${!1}"; elif [ -f "${ROOT}/.env" ]; then grep -E "^$1=" "${ROOT}/.env" | head -n 1 | cut -d= -f2- || true; fi; }
WSO2_KEYSTORE_PASSWORD="$(setting WSO2_KEYSTORE_PASSWORD)"
DOMAIN="$(setting DOMAIN)"
: "${WSO2_KEYSTORE_PASSWORD:?Set WSO2_KEYSTORE_PASSWORD in .env (scripts/init-env.sh)}"
export WSO2_KEYSTORE_PASSWORD

# The containers read the certificates as other users (WSO2 uid 802, the apps uid 1654), so the folders must be
# traversable whatever the caller's umask; the private keys are protected by their own mode (0600).
mkdir -p "${OUT}/wso2" "${OUT}/sqlserver"
chmod 0755 "${OUT}" "${OUT}/wso2" "${OUT}/sqlserver"
cd "${OUT}"

if [ ! -f regreturns-dev-ca.key ] || [ ! -f regreturns-dev-ca.crt ]; then
  echo "Creating the RegReturns development CA"
  # Written under temporary names and renamed together, so a failed run cannot leave a key without its certificate.
  openssl req -x509 -newkey rsa:3072 -sha256 -days 3650 -nodes \
    -keyout regreturns-dev-ca.key.new -out regreturns-dev-ca.crt.new \
    -subj "/O=RegReturns (development)/CN=RegReturns Dev CA" \
    -addext "basicConstraints=critical,CA:TRUE,pathlen:0" \
    -addext "keyUsage=critical,keyCertSign,cRLSign" 2> /dev/null
  chmod 0600 regreturns-dev-ca.key.new
  mv regreturns-dev-ca.key.new regreturns-dev-ca.key
  mv regreturns-dev-ca.crt.new regreturns-dev-ca.crt
fi
# Set on every run, so a CA created under a strict umask still reaches the containers that trust it.
chmod 0644 regreturns-dev-ca.crt
chmod 0600 regreturns-dev-ca.key

# SQL Server's certificate. Issued even when the WSO2 keystores exist, so an older checkout gains it on the next run.
SQL_DIR="${OUT}/sqlserver"
if [ ! -f "${SQL_DIR}/mssql.crt" ] || [ ! -f "${SQL_DIR}/mssql.key" ] || [ "${FORCE}" = "--force" ]; then
  SQL_SANS="DNS:sqlserver,DNS:localhost,IP:127.0.0.1"
  echo "Issuing the SQL Server certificate (${SQL_SANS})"
  mkdir -p "${SQL_DIR}"
  SQL_STAGE="$(mktemp -d "${OUT}/.stage-sql.XXXXXX")"
  trap 'rm -rf "${SQL_STAGE}"' EXIT
  openssl req -newkey rsa:2048 -nodes -keyout "${SQL_STAGE}/mssql.key" -out "${SQL_STAGE}/mssql.csr" \
    -subj "/O=RegReturns (development)/CN=sqlserver" 2> /dev/null
  openssl x509 -req -in "${SQL_STAGE}/mssql.csr" -CA regreturns-dev-ca.crt -CAkey regreturns-dev-ca.key -CAcreateserial \
    -out "${SQL_STAGE}/mssql.crt" -days 825 -sha256 \
    -extfile <(printf "subjectAltName=%s\nkeyUsage=critical,digitalSignature,keyEncipherment\nextendedKeyUsage=serverAuth\nbasicConstraints=CA:FALSE\n" "${SQL_SANS}") 2> /dev/null
  chmod 0600 "${SQL_STAGE}/mssql.key"
  chmod 0644 "${SQL_STAGE}/mssql.crt"
  mv -f "${SQL_STAGE}/mssql.key" "${SQL_DIR}/mssql.key"
  mv -f "${SQL_STAGE}/mssql.crt" "${SQL_DIR}/mssql.crt"
  rm -rf "${SQL_STAGE}"
  trap - EXIT
fi

OUTPUTS=(regreturns-tls.p12 regreturns-primary.p12 regreturns-truststore.p12 regreturns-signing.crt regreturns-dev-ca.crt)
complete=true
for file in "${OUTPUTS[@]}"; do [ -f "wso2/${file}" ] || complete=false; done
if [ "${complete}" = true ] && [ "${FORCE}" != "--force" ]; then
  echo "WSO2 keystores already exist (use --force to replace them)"
  exit 0
fi

SANS="DNS:localhost,DNS:iam.localhost,DNS:wso2,IP:127.0.0.1"
if [ -n "${DOMAIN:-}" ]; then SANS="${SANS},DNS:iam.${DOMAIN}"; fi

# Staging folder inside .certs (Docker can mount it on every platform); removed on exit, whatever happens.
STAGE="$(mktemp -d "${OUT}/.stage.XXXXXX")"
trap 'rm -rf "${STAGE}"' EXIT
chmod 0755 "${STAGE}"

echo "Issuing the WSO2 HTTPS certificate (${SANS})"
openssl req -newkey rsa:2048 -nodes -keyout "${STAGE}/tls.key" -out "${STAGE}/tls.csr" \
  -subj "/O=RegReturns (development)/CN=localhost" 2> /dev/null
openssl x509 -req -in "${STAGE}/tls.csr" -CA regreturns-dev-ca.crt -CAkey regreturns-dev-ca.key -CAcreateserial \
  -out "${STAGE}/tls.crt" -days 825 -sha256 \
  -extfile <(printf "subjectAltName=%s\nkeyUsage=critical,digitalSignature,keyEncipherment\nextendedKeyUsage=serverAuth\nbasicConstraints=CA:FALSE\n" "${SANS}") 2> /dev/null
openssl pkcs12 -export -name regreturns-tls -in "${STAGE}/tls.crt" -inkey "${STAGE}/tls.key" \
  -certfile regreturns-dev-ca.crt -out "${STAGE}/regreturns-tls.p12" -passout env:WSO2_KEYSTORE_PASSWORD

echo "Creating the WSO2 token-signing key"
openssl req -x509 -newkey rsa:2048 -sha256 -days 1095 -nodes -keyout "${STAGE}/signing.key" -out "${STAGE}/signing.crt" \
  -subj "/O=RegReturns (development)/CN=RegReturns IAM token signing" 2> /dev/null
openssl pkcs12 -export -name regreturns-signing -in "${STAGE}/signing.crt" -inkey "${STAGE}/signing.key" \
  -out "${STAGE}/regreturns-primary.p12" -passout env:WSO2_KEYSTORE_PASSWORD
cp "${STAGE}/signing.crt" "${STAGE}/regreturns-signing.crt"
cp regreturns-dev-ca.crt "${STAGE}/regreturns-dev-ca.crt"

echo "Building the WSO2 truststore"
# keytool runs inside the WSO2 image so the truststore starts from WSO2's own public roots. The password reaches the
# container through the environment (-e NAME without a value) and keytool reads it with :env.
docker run --rm --user root --entrypoint sh -e WSO2_KEYSTORE_PASSWORD -v "${STAGE}:/out" "${WSO2_IMAGE}" -c '
  set -e
  keytool -importkeystore -noprompt \
    -srckeystore "${WSO2_SERVER_HOME}/repository/resources/security/client-truststore.p12" -srcstoretype PKCS12 \
    -srcstorepass wso2carbon \
    -destkeystore /out/regreturns-truststore.p12 -deststoretype PKCS12 -deststorepass:env WSO2_KEYSTORE_PASSWORD \
    > /tmp/import.log 2>&1 || { cat /tmp/import.log >&2; exit 1; }
  keytool -delete -alias wso2carbon \
    -keystore /out/regreturns-truststore.p12 -storetype PKCS12 -storepass:env WSO2_KEYSTORE_PASSWORD
  keytool -importcert -noprompt -alias regreturns-dev-ca -file /out/regreturns-dev-ca.crt \
    -keystore /out/regreturns-truststore.p12 -storetype PKCS12 -storepass:env WSO2_KEYSTORE_PASSWORD
  keytool -importcert -noprompt -alias regreturns-signing -file /out/regreturns-signing.crt \
    -keystore /out/regreturns-truststore.p12 -storetype PKCS12 -storepass:env WSO2_KEYSTORE_PASSWORD
  chown '"$(id -u):$(id -g)"' /out/regreturns-truststore.p12'

# Everything is built: move it into place together. The WSO2 container runs as uid 802 and copies these files at
# start, so they must be readable. The keystores are protected by their password and, on a server, by the checkout's
# mode (0750, deploy/server-setup.sh); the CA key stays 0600.
for file in "${OUTPUTS[@]}"; do
  chmod 0644 "${STAGE}/${file}"
  mv -f "${STAGE}/${file}" "wso2/${file}"
done
echo "Done. Trust ${OUT}/regreturns-dev-ca.crt in your browser to open https://localhost:9443 without warnings."
