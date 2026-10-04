#!/bin/bash
# Creates WSO2's databases and a dedicated SQL login, then loads WSO2's schema scripts once.
# Safe to run on every `docker compose up`: each step checks before it changes anything.
set -euo pipefail

: "${MSSQL_SA_PASSWORD:?MSSQL_SA_PASSWORD is required}"
: "${WSO2_DB_USERNAME:?WSO2_DB_USERNAME is required}"
: "${WSO2_DB_PASSWORD:?WSO2_DB_PASSWORD is required}"
DB_HOST="${WSO2_DB_HOST:-sqlserver}"
SCRIPTS=/wso2/dbscripts

# sqlcmd reads the password from SQLCMDPASSWORD so it never appears in the process list.
# -C: the SQL Server container uses a self-signed certificate (see deployment.toml).
sql() { SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S "$DB_HOST" -U sa -C -b -l 30 "$@"; }

echo "Waiting for SQL Server at ${DB_HOST}..."
for attempt in $(seq 1 60); do
  if sql -Q "SELECT 1" > /dev/null 2>&1; then break; fi
  if [ "$attempt" -eq 60 ]; then echo "SQL Server did not become available" >&2; exit 1; fi
  sleep 2
done

echo "Ensuring login ${WSO2_DB_USERNAME}..."
sql -v Login="$WSO2_DB_USERNAME" Password="$WSO2_DB_PASSWORD" -Q "
IF SUSER_ID(N'\$(Login)') IS NULL
  CREATE LOGIN [\$(Login)] WITH PASSWORD = N'\$(Password)', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
ELSE
  ALTER LOGIN [\$(Login)] WITH PASSWORD = N'\$(Password)';"

# Creates the database if needed, maps the WSO2 login as its owner, and loads the scripts
# unless the marker table already exists.
prepare_db() {
  local db="$1" marker="$2"; shift 2
  sql -v Db="$db" Login="$WSO2_DB_USERNAME" -Q "
IF DB_ID(N'\$(Db)') IS NULL CREATE DATABASE [\$(Db)];"
  sql -d "$db" -v Login="$WSO2_DB_USERNAME" -Q "
IF USER_ID(N'\$(Login)') IS NULL CREATE USER [\$(Login)] FOR LOGIN [\$(Login)];
IF IS_ROLEMEMBER(N'db_owner', N'\$(Login)') = 0 ALTER ROLE db_owner ADD MEMBER [\$(Login)];"

  if [ "$(sql -d "$db" -h -1 -W -Q "SET NOCOUNT ON; SELECT CASE WHEN OBJECT_ID(N'dbo.${marker}') IS NULL THEN 0 ELSE 1 END")" = "1" ]; then
    echo "${db}: schema present, skipping scripts"
    return
  fi
  for script in "$@"; do
    echo "${db}: running ${script#"$SCRIPTS"/}"
    sql -d "$db" -i "$script" > /dev/null
  done
}

prepare_db WSO2_IDENTITY_DB IDN_OAUTH_CONSUMER_APPS "$SCRIPTS/identity/mssql.sql" "$SCRIPTS/consent/mssql.sql"
prepare_db WSO2_SHARED_DB UM_TENANT "$SCRIPTS/mssql.sql"
prepare_db WSO2_AGENT_DB UM_ROLE "$SCRIPTS/identity/agent/mssql.sql"

echo "WSO2 databases are ready."
