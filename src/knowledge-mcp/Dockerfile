# =============================================================================
# Multi-Stage Dockerfile for Contoso Knowledge MCP server
# =============================================================================
# Build context should be the repository root:
#   docker build -t knowledge-mcp:latest -f src/knowledge-mcp/Dockerfile .
# =============================================================================

# ---------------------------------------------------------------------------
# Stage 1: Build
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

ARG BUILD_CONFIGURATION=Release

WORKDIR /src

COPY Directory.Build.props ./
COPY Directory.Packages.props ./
COPY global.json ./

COPY src/ServiceDefaults/Contoso.ServiceDefaults.csproj ./src/ServiceDefaults/
COPY src/knowledge-mcp/Contoso.KnowledgeMcp.csproj ./src/knowledge-mcp/

RUN dotnet restore "./src/knowledge-mcp/Contoso.KnowledgeMcp.csproj"

COPY src/ServiceDefaults/ ./src/ServiceDefaults/
COPY src/knowledge-mcp/ ./src/knowledge-mcp/

# ---------------------------------------------------------------------------
# Stage 2: Publish
# ---------------------------------------------------------------------------
FROM build AS publish

ARG BUILD_CONFIGURATION=Release

RUN dotnet publish "./src/knowledge-mcp/Contoso.KnowledgeMcp.csproj" \
    -c ${BUILD_CONFIGURATION} \
    -o /app/publish \
    --no-restore \
    /p:PublishReadyToRun=true

# ---------------------------------------------------------------------------
# Stage 3: Runtime
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

ARG BUILD_VERSION=0.0.1
ARG BUILD_DATE
ARG VCS_REF

LABEL org.opencontainers.image.title="knowledge-mcp" \
      org.opencontainers.image.description="Contoso Outdoors Knowledge MCP server — products + AI Search retrieval" \
      org.opencontainers.image.version="${BUILD_VERSION}" \
      org.opencontainers.image.created="${BUILD_DATE}" \
      org.opencontainers.image.revision="${VCS_REF}" \
      org.opencontainers.image.vendor="Contoso" \
      org.opencontainers.image.licenses="MIT"

WORKDIR /app

COPY --from=publish /app/publish .

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD ["sh", "-c", "wget --no-verbose --tries=1 --spider http://localhost:8080/health || exit 1"]

USER app

ENTRYPOINT ["dotnet", "Contoso.KnowledgeMcp.dll"]
