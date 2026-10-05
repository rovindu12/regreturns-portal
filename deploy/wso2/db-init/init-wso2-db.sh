#!/bin/bash
# Creates WSO2's databases and a dedicated SQL login, then loads WSO2's schema scripts once.
# Safe to run on every `docker compose up`: each step checks before it changes anything.
set -euo pipefail

: "${MSSQL_SA_PASSWORD:?MSSQL_SA_PASSWORD is required}"
: "${WSO2_DB_USERNAME:?WSO2_DB_USERNAME is required}"
: "${WSO2_DB_PASSWORD:?WSO2_DB_PASSWORD is required}"
DB_HOST="${WSO2_DB_HOST:-sqlserver}"
SCRIPTS=/wso2/dbscripts
WSO2_VERSION="${WSO2_VERSION:-7.3.0}"

# sqlcmd reads the password from SQLCMDPASSWORD so it never appears in the process list.
# -C: the SQL Server container uses a self-signed certificate (see deployment.toml).
sql() { SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S "$DB_HOST" -U sa -C -b -l 30 "$@"; }

echo "Waiting for SQL Server at ${DB_HOST}..."
for attempt in $(seq 1 60); do
  if sql -Q "SELECT 1" > /dev/null 2>&1; then break; fi
  if [ "$attempt" -eq 60 ]; then echo "SQL Server did not become available" >&2; exit 1; fi
  sleep 2
done

# sqlcmd resolves $(WSO2_DB_PASSWORD) from the environment, so the password is not passed with -v on the command line.
echo "Ensuring login ${WSO2_DB_USERNAME}..."
sql -v Login="$WSO2_DB_USERNAME" -Q "
IF SUSER_ID(N'\$(Login)') IS NULL
  CREATE LOGIN [\$(Login)] WITH PASSWORD = N'\$(WSO2_DB_PASSWORD)', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
ELSE
  ALTER LOGIN [\$(Login)] WITH PASSWORD = N'\$(WSO2_DB_PASSWORD)';"

# Creates the database if needed, maps the WSO2 login as its owner, and loads the scripts unless an earlier run
# finished them. The marker table is written only after every script for the database has succeeded, so an
# interrupted run is detected instead of being mistaken for a complete schema.
MARKER=REGRETURNS_WSO2_SCHEMA
prepare_db() {
  local db="$1"; shift
  sql -v Db="$db" -Q "
IF DB_ID(N'\$(Db)') IS NULL CREATE DATABASE [\$(Db)];"
  sql -d "$db" -v Login="$WSO2_DB_USERNAME" -Q "
IF USER_ID(N'\$(Login)') IS NULL CREATE USER [\$(Login)] FOR LOGIN [\$(Login)];
IF IS_ROLEMEMBER(N'db_owner', N'\$(Login)') = 0 ALTER ROLE db_owner ADD MEMBER [\$(Login)];"

  local state
  state="$(sql -d "$db" -h -1 -W -Q "SET NOCOUNT ON;
SELECT CASE WHEN OBJECT_ID(N'dbo.${MARKER}') IS NOT NULL THEN 'complete'
            WHEN EXISTS (SELECT 1 FROM sys.tables WHERE is_ms_shipped = 0) THEN 'partial'
            ELSE 'empty' END")"
  case "$state" in
    complete) echo "${db}: schema present, skipping scripts"; return ;;
    partial)
      echo "${db} has some WSO2 tables but no completion marker: an earlier run stopped part-way." >&2
      echo "Drop the database (or the sqlserver-data volume) and run this job again; see docs/TROUBLESHOOTING.md." >&2
      exit 1 ;;
  esac

  for script in "$@"; do
    echo "${db}: running ${script#"$SCRIPTS"/}"
    sql -d "$db" -i "$script" > /dev/null
  done
  sql -d "$db" -v Version="$WSO2_VERSION" -Q "
SET NOCOUNT ON;
CREATE TABLE dbo.${MARKER} (WSO2_VERSION NVARCHAR(20) NOT NULL, COMPLETED_AT DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME());
INSERT INTO dbo.${MARKER} (WSO2_VERSION) VALUES (N'\$(Version)');"
}

prepare_db WSO2_IDENTITY_DB "$SCRIPTS/identity/mssql.sql" "$SCRIPTS/consent/mssql.sql"
prepare_db WSO2_SHARED_DB "$SCRIPTS/mssql.sql"
prepare_db WSO2_AGENT_DB "$SCRIPTS/identity/agent/mssql.sql"

echo "WSO2 databases are ready."
