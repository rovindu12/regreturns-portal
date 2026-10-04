# syntax=docker/dockerfile:1
# WSO2 Identity Server with the Microsoft JDBC driver and RegReturns' deployment.toml (ADR 0014).
# Secrets and keystores are not baked in: deployment.toml reads them from environment variables and the
# keystores are mounted at runtime (see docker-compose.yml and scripts/dev-certs.sh).
FROM wso2/wso2is:7.3.0

# Pinned by version and checksum; bump both together.
ADD --chown=wso2carbon:wso2 --chmod=0644 \
    --checksum=sha256:4c566e4022c4dcf1489aeb639fe49a0f6ffae3d796fdc2f56b5b8882d4c3bf5a \
    https://repo1.maven.org/maven2/com/microsoft/sqlserver/mssql-jdbc/13.6.0.jre11/mssql-jdbc-13.6.0.jre11.jar \
    ${WSO2_SERVER_HOME}/repository/components/lib/

COPY --chown=wso2carbon:wso2 --chmod=0644 deployment.toml ${WSO2_SERVER_HOME}/repository/conf/deployment.toml

HEALTHCHECK --interval=15s --timeout=5s --start-period=120s --retries=20 \
  CMD curl --fail --silent --cacert ${WORKING_DIRECTORY}/wso2-config-volume/repository/resources/security/regreturns-dev-ca.crt \
      https://localhost:9443/api/health-check/v1.0/health > /dev/null || exit 1
