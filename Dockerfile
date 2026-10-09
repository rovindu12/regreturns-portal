# syntax=docker/dockerfile:1
# RegReturns container images (ADR 0034). One build stage compiles everything once; each final stage is a target:
#
#   docker build --target web            -t regreturns-web .            portal       (ASP.NET Core, port 8080)
#   docker build --target api            -t regreturns-api .            REST API     (ASP.NET Core, port 8080)
#   docker build --target migrator       -t regreturns-migrator .       migrate-db, seed, legacy, verify-audit
#   docker build --target iam-bootstrap  -t regreturns-iam-bootstrap .  WSO2 setup, demo users (one-off)
#
# Runtime images are Ubuntu chiselled: no shell, no package manager, a non-root user (app, uid 1654) and only the
# libraries .NET needs. The "extra" flavour adds ICU and time zone data, which SQL Server's client and the reports use.
# Pass --build-arg SOURCE_REVISION=<git sha> to stamp the commit into the assemblies (shown on the status page) and the
# image label. The two base images are pinned by tag; Dependabot proposes their updates as one group
# (.github/dependabot.yml).

FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble AS build
WORKDIR /src
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

# Restore from the project and lock files alone, so this layer is rebuilt only when a dependency changes. A project
# missing here fails the restore of whichever project references it.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/RegReturns.Domain/*.csproj src/RegReturns.Domain/packages.lock.json src/RegReturns.Domain/
COPY src/RegReturns.Application/*.csproj src/RegReturns.Application/packages.lock.json src/RegReturns.Application/
COPY src/RegReturns.Infrastructure/*.csproj src/RegReturns.Infrastructure/packages.lock.json src/RegReturns.Infrastructure/
COPY src/RegReturns.ServiceDefaults/*.csproj src/RegReturns.ServiceDefaults/packages.lock.json src/RegReturns.ServiceDefaults/
COPY src/RegReturns.Web/*.csproj src/RegReturns.Web/packages.lock.json src/RegReturns.Web/
COPY src/RegReturns.Api/*.csproj src/RegReturns.Api/packages.lock.json src/RegReturns.Api/
COPY tools/RegReturns.Migrator/*.csproj tools/RegReturns.Migrator/packages.lock.json tools/RegReturns.Migrator/
COPY tools/RegReturns.IamBootstrap/*.csproj tools/RegReturns.IamBootstrap/packages.lock.json tools/RegReturns.IamBootstrap/
RUN for project in src/RegReturns.Web src/RegReturns.Api tools/RegReturns.Migrator tools/RegReturns.IamBootstrap; do \
      dotnet restore "${project}" --locked-mode || exit 1; \
    done

COPY src/ src/
COPY tools/ tools/

# The build context has no .git folder, so the commit comes in as an argument; source control queries are off.
# Publishing is portable (the lock files hold no runtime identifier), so native libraries for every platform are
# published (QuestPDF, SQL Server's client); only the image's own platform is kept, which saves about 100 MB an image.
ARG SOURCE_REVISION=unknown
ARG TARGETARCH
RUN case "${TARGETARCH:-amd64}" in \
      amd64) rid=linux-x64 ;; \
      arm64) rid=linux-arm64 ;; \
      *) echo "Unsupported architecture: ${TARGETARCH}" >&2; exit 1 ;; \
    esac; \
    for target in web:src/RegReturns.Web api:src/RegReturns.Api migrator:tools/RegReturns.Migrator \
                  iam-bootstrap:tools/RegReturns.IamBootstrap; do \
      out="/out/${target%%:*}"; \
      dotnet publish "${target#*:}" --configuration Release --no-restore --output "${out}" \
        -p:UseAppHost=false -p:EnableSourceControlManagerQueries=false -p:SourceRevisionId="${SOURCE_REVISION}" \
        -p:ContinuousIntegrationBuild=true || exit 1; \
      find "${out}/runtimes" -mindepth 1 -maxdepth 1 ! -name "${rid}" ! -name unix -exec rm -rf {} +; \
    done; \
    # An empty folder for the portal's data-protection keys (see the web stage).
    install -d /out/keys

# Every image runs on the ASP.NET Core runtime: the tools use ServiceDefaults too. Shared labels (OCI image spec); the
# release workflow adds created and revision from the commit.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-noble-chiseled-extra AS runtime-base
ARG SOURCE_REVISION=unknown
LABEL org.opencontainers.image.source="https://github.com/rovindu12/regreturns-portal" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.vendor="RegReturns (portfolio project, fictional institutions)" \
      org.opencontainers.image.revision="${SOURCE_REVISION}"
WORKDIR /app
USER $APP_UID

FROM runtime-base AS aspnet-base
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

FROM aspnet-base AS web
LABEL org.opencontainers.image.title="regreturns-web" \
      org.opencontainers.image.description="RegReturns portal: returns, workflow, reports and the public demo"
COPY --from=build /out/web ./
# The portal's data-protection keys live on a volume mounted at this folder (ADR 0034). Docker copies the folder's
# owner and mode into a new named volume, so the app's user can write it and nobody else can read it. COPY copies a
# folder's contents, not the folder, so the owner and mode are set here rather than in the build stage.
COPY --from=build --chown=$APP_UID:$APP_UID --chmod=0700 /out/keys /var/lib/regreturns/keys
ENV DataProtection__KeysPath=/var/lib/regreturns/keys
# No shell in the image: the app's own binary is the probe (ServiceDefaults HealthProbe). Readiness, so the
# container turns healthy once the database and WSO2 answer.
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
  CMD ["dotnet", "RegReturns.Web.dll", "--health-probe", "/health/ready"]
ENTRYPOINT ["dotnet", "RegReturns.Web.dll"]

FROM aspnet-base AS api
LABEL org.opencontainers.image.title="regreturns-api" \
      org.opencontainers.image.description="RegReturns REST API v1 for bank systems"
COPY --from=build /out/api ./
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
  CMD ["dotnet", "RegReturns.Api.dll", "--health-probe", "/health/ready"]
ENTRYPOINT ["dotnet", "RegReturns.Api.dll"]

FROM runtime-base AS migrator
LABEL org.opencontainers.image.title="regreturns-migrator" \
      org.opencontainers.image.description="RegReturns database migrations, demo seed and legacy CSV migration"
COPY --from=build /out/migrator ./
ENTRYPOINT ["dotnet", "regreturns-migrator.dll"]

FROM runtime-base AS iam-bootstrap
LABEL org.opencontainers.image.title="regreturns-iam-bootstrap" \
      org.opencontainers.image.description="RegReturns WSO2 Identity Server setup and demo user reset"
COPY --from=build /out/iam-bootstrap ./
ENTRYPOINT ["dotnet", "regreturns-iam-bootstrap.dll"]
