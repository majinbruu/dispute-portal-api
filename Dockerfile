# syntax=docker/dockerfile:1

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from project files only, so the package layer is cached until dependencies change.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/DisputePortal.Api/DisputePortal.Api.csproj src/DisputePortal.Api/
COPY src/DisputePortal.SharedKernel/DisputePortal.SharedKernel.csproj src/DisputePortal.SharedKernel/
COPY src/DisputePortal.Modules.DebitOrders/DisputePortal.Modules.DebitOrders.csproj src/DisputePortal.Modules.DebitOrders/
COPY src/DisputePortal.Modules.Disputes/DisputePortal.Modules.Disputes.csproj src/DisputePortal.Modules.Disputes/
COPY src/DisputePortal.Modules.Notifications/DisputePortal.Modules.Notifications.csproj src/DisputePortal.Modules.Notifications/
RUN dotnet restore src/DisputePortal.Api/DisputePortal.Api.csproj

COPY src/ src/
# The OpenAPI document is generated and checked in CI, not during the image build.
RUN dotnet publish src/DisputePortal.Api/DisputePortal.Api.csproj \
    --configuration Release --no-restore --output /app \
    -p:OpenApiGenerateDocuments=false

# ---- runtime ----
# Chiseled image: no shell, no package manager, runs as a non-root user.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
COPY --from=build /app ./

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0
EXPOSE 8080
USER $APP_UID

# There is no curl in the image, so the app probes itself: `--healthcheck` calls /health/ready and sets the exit code.
HEALTHCHECK --interval=10s --timeout=5s --start-period=30s --retries=6 \
    CMD ["dotnet", "DisputePortal.Api.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "DisputePortal.Api.dll"]
