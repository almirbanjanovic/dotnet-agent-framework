# =============================================================================
# Multi-Stage Dockerfile for Contoso Blazor UI (Blazor WebAssembly SPA)
# =============================================================================
# Build context should be the repository root:
#   docker build -t blazor-ui:latest -f src/blazor-ui/Dockerfile .
#
# Blazor WASM publishes as static files (HTML/CSS/JS/wasm). The runtime
# image is therefore an unprivileged nginx that serves /usr/share/nginx/html
# on port 8080 with SPA-style fallback to index.html.
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

COPY src/blazor-ui/Contoso.BlazorUi.csproj ./src/blazor-ui/

# Blazor WASM workload is required for `dotnet restore`.
RUN dotnet workload install wasm-tools \
    && dotnet restore "./src/blazor-ui/Contoso.BlazorUi.csproj"

COPY src/blazor-ui/ ./src/blazor-ui/

# ---------------------------------------------------------------------------
# Stage 2: Publish — produces static files in wwwroot/
# ---------------------------------------------------------------------------
FROM build AS publish

ARG BUILD_CONFIGURATION=Release

RUN dotnet publish "./src/blazor-ui/Contoso.BlazorUi.csproj" \
    -c ${BUILD_CONFIGURATION} \
    -o /app/publish \
    --no-restore

# ---------------------------------------------------------------------------
# Stage 3: Runtime — unprivileged nginx serving the SPA
# ---------------------------------------------------------------------------
FROM nginx:1.27-alpine AS runtime

ARG BUILD_VERSION=0.0.1
ARG BUILD_DATE
ARG VCS_REF

LABEL org.opencontainers.image.title="blazor-ui" \
      org.opencontainers.image.description="Contoso Outdoors Blazor UI — chat-style WASM SPA served by nginx" \
      org.opencontainers.image.version="${BUILD_VERSION}" \
      org.opencontainers.image.created="${BUILD_DATE}" \
      org.opencontainers.image.revision="${VCS_REF}" \
      org.opencontainers.image.vendor="Contoso" \
      org.opencontainers.image.licenses="MIT"

# Use a custom nginx.conf so the server listens on 8080 (matches the rest
# of the platform) and supports SPA fallback to index.html.
COPY src/blazor-ui/nginx.conf /etc/nginx/nginx.conf

# Copy the published wwwroot contents, not the parent dir, so nginx can
# serve index.html directly from /usr/share/nginx/html.
COPY --from=publish /app/publish/wwwroot/ /usr/share/nginx/html/

# Run as the unprivileged nginx user (UID 101 in nginx:alpine). This keeps
# readOnlyRootFilesystem viable; nginx writes only to /tmp and /var/cache.
USER nginx

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD ["wget", "--no-verbose", "--tries=1", "--spider", "http://localhost:8080/health"]

ENTRYPOINT ["nginx", "-g", "daemon off;"]
