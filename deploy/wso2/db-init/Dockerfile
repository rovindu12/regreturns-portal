# syntax=docker/dockerfile:1
# One-off job that prepares WSO2's databases on the shared SQL Server (ADR 0014).
# It reuses the SQL Server image for sqlcmd and copies WSO2's own SQL scripts from the WSO2 image,
# so the schema always matches the WSO2 version in use.
FROM wso2/wso2is:7.3.0 AS wso2

# Keep in step with the sqlserver service in docker-compose.yml.
FROM mcr.microsoft.com/mssql/server:2025-CU9-ubuntu-24.04
COPY --from=wso2 /home/wso2carbon/wso2is-7.3.0/dbscripts /wso2/dbscripts
COPY --chmod=0755 init-wso2-db.sh /usr/local/bin/init-wso2-db.sh
ENTRYPOINT ["/usr/local/bin/init-wso2-db.sh"]
