#!/bin/bash
# Prepares the RegReturns database for the production apps (ADR 0034): creates the database if it is missing and a
# SQL login for the portal and the API whose database user is a member of regreturns_runtime only. That role (from the
# AddRuntimeRole migration) may read and write data but never change the schema, switch off a trigger or rewrite the
# audit chain; the migrator and the backups keep using the administrator login.
#
# Run by the app-db-init job in docker-compose.prod.yml, before or after the migrations: the role is created here
# empty when the migrations have not run yet, and they grant it its permissions. Safe to run on every deploy. After a
# restore onto another server the database user is orphaned (its SID belongs to the old server's login), so it is
# mapped to this server's login again, and the login's password always follows APP_DB_PASSWORD.
set -euo pipefail

: "${MSSQL_SA_PASSWORD:?MSSQL_SA_PASSWORD is required}"
: "${APP_DB_USERNAME:?APP_DB_USERNAME is required}"
: "${APP_DB_PASSWORD:?APP_DB_PASSWORD is required}"
DB_HOST="${DB_HOST:-sqlserver}"
DB_NAME="${DB_NAME:-RegReturns}"
ROLE=regreturns_runtime

# The password reaches sqlcmd through SQLCMDPASSWORD (sa) and the environment ($(APP_DB_PASSWORD) is resolved by sqlcmd
# from its environment), never the command line. -Ns -J: strict TLS pinned to SQL Server's certificate (ADR 0033).
SERVER_CERT="${SQL_SERVER_CERT:-/tls/mssql.crt}"
[ -r "${SERVER_CERT}" ] || { echo "SQL Server's certificate is missing at ${SERVER_CERT}: run scripts/dev-certs.sh" >&2; exit 1; }
sql() { SQLCMDPASSWORD="${MSSQL_SA_PASSWORD}" /opt/mssql-tools18/bin/sqlcmd -S "${DB_HOST}" -U sa -Ns -J "${SERVER_CERT}" -b -l 30 "$@"; }

echo "Waiting for SQL Server at ${DB_HOST}..."
for attempt in $(seq 1 60); do
  if sql -Q "SELECT 1" > /dev/null 2>&1; then break; fi
  if [ "${attempt}" -eq 60 ]; then echo "SQL Server did not become available" >&2; exit 1; fi
  sleep 2
done

echo "Ensuring database ${DB_NAME} and login ${APP_DB_USERNAME}..."
sql -v Db="${DB_NAME}" -v Login="${APP_DB_USERNAME}" -Q "
IF DB_ID(N'\$(Db)') IS NULL CREATE DATABASE [\$(Db)];
IF SUSER_ID(N'\$(Login)') IS NULL
  CREATE LOGIN [\$(Login)] WITH PASSWORD = N'\$(APP_DB_PASSWORD)', DEFAULT_DATABASE = [\$(Db)], CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
ELSE
  ALTER LOGIN [\$(Login)] WITH PASSWORD = N'\$(APP_DB_PASSWORD)';"

sql -d "${DB_NAME}" -v Login="${APP_DB_USERNAME}" -v Role="${ROLE}" -Q "
IF DATABASE_PRINCIPAL_ID(N'\$(Role)') IS NULL CREATE ROLE [\$(Role)];
IF USER_ID(N'\$(Login)') IS NULL CREATE USER [\$(Login)] FOR LOGIN [\$(Login)];
ELSE ALTER USER [\$(Login)] WITH LOGIN = [\$(Login)];
IF IS_ROLEMEMBER(N'\$(Role)', N'\$(Login)') = 0 ALTER ROLE [\$(Role)] ADD MEMBER [\$(Login)];"

# The login must hold nothing else: a fixed role added by hand would quietly widen what the apps can do.
extra="$(sql -d "${DB_NAME}" -h -1 -W -v Login="${APP_DB_USERNAME}" -v Role="${ROLE}" -Q "SET NOCOUNT ON;
SELECT STRING_AGG(r.name, N', ') FROM sys.database_role_members m
JOIN sys.database_principals r ON r.principal_id = m.role_principal_id
WHERE m.member_principal_id = USER_ID(N'\$(Login)') AND r.name <> N'\$(Role)';")"
if [ -n "${extra// /}" ] && [ "${extra}" != "NULL" ]; then
  echo "${APP_DB_USERNAME} is also a member of: ${extra}. Remove those memberships; the apps need ${ROLE} only." >&2
  exit 1
fi

echo "${DB_NAME} is ready for ${APP_DB_USERNAME} (${ROLE})."
