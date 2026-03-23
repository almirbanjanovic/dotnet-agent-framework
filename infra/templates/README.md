# Service Templates

Reference templates for building and deploying the 8 Contoso Outdoors services on AKS. Every service follows these patterns for consistency, security, and operational reliability.

## Services

| Service | Type | Service Account | Notes |
|---------|------|-----------------|-------|
| `crm-api` | .NET 9 Minimal API | `sa-crm-api` | Cosmos DB, core data layer |
| `bff-api` | .NET 9 Minimal API | `sa-bff` | Frontend gateway, auth boundary |
| `crm-mcp` | .NET 9 Minimal API | `sa-crm-mcp` | MCP server for CRM tools |
| `knowledge-mcp` | .NET 9 Minimal API | `sa-know-mcp` | MCP server for AI Search |
| `crm-agent` | .NET 9 Minimal API | `sa-crm-agent` | Entra Agent Identity |
| `product-agent` | .NET 9 Minimal API | `sa-prod-agent` | Entra Agent Identity |
| `orchestrator-agent` | .NET 9 Minimal API | `sa-orch-agent` | Entra Agent Identity |
| `blazor-ui` | Blazor WASM (static) | `sa-bff` | Served via nginx or dotnet |

## Using the Dockerfile Template

1. **Copy** `Dockerfile.template` into your service directory and rename to `Dockerfile`.
2. **Replace** the `ARG` defaults and the `ENTRYPOINT` assembly name:

   ```dockerfile
   ARG PROJECT_NAME=Contoso.CrmApi
   ARG PROJECT_PATH=src/Contoso.CrmApi
   # ...
   ENTRYPOINT ["dotnet", "Contoso.CrmApi.dll"]
   ```

3. **Build** from the repository root (so `Directory.Build.props` and `global.json` are in context):

   ```bash
   docker build -t contosoacr.azurecr.io/crm-api:latest \
     --build-arg PROJECT_NAME=Contoso.CrmApi \
     --build-arg PROJECT_PATH=src/Contoso.CrmApi \
     --build-arg BUILD_VERSION=1.0.0 \
     --build-arg BUILD_DATE=$(date -u +"%Y-%m-%dT%H:%M:%SZ") \
     --build-arg VCS_REF=$(git rev-parse --short HEAD) \
     -f src/Contoso.CrmApi/Dockerfile .
   ```

### Blazor UI Special Case

The Blazor WebAssembly UI produces static files. Options:
- **nginx**: Replace the runtime stage with `nginx:alpine`, copy published `wwwroot/` into `/usr/share/nginx/html/`
- **dotnet**: Use the standard template with `dotnet Contoso.BlazorUi.dll` serving static files

## Using the Helm Chart Base

1. **Copy** the entire `helm-base/` directory into `infra/helm/<service-name>/`.
2. **Update `Chart.yaml`**:
   ```yaml
   name: crm-api
   description: Contoso CRM API — customer, order, and product data
   appVersion: "1.0.0"
   ```
3. **Create a `values-<env>.yaml`** override for your environment:
   ```yaml
   image:
     repository: contosoacr.azurecr.io/crm-api
     tag: "1.0.0"
   serviceAccount:
     name: sa-crm-api
   config:
     ASPNETCORE_ENVIRONMENT: "Production"
   replicaCount: 2
   autoscaling:
     enabled: true
   ```
4. **Deploy**:
   ```bash
   helm upgrade --install crm-api infra/helm/crm-api/ \
     -n contoso \
     -f infra/helm/crm-api/values-dev.yaml
   ```

## Key Design Decisions

### Security
- **Non-root execution**: Container runs as UID 1654 (`app` user from aspnet:9.0 image). Pod security context enforces `runAsNonRoot: true`.
- **Read-only filesystem**: `readOnlyRootFilesystem: true` with a writable `/tmp` emptyDir for ASP.NET temp files.
- **No privilege escalation**: `allowPrivilegeEscalation: false`, all capabilities dropped.
- **Workload Identity**: Pods authenticate to Azure via OIDC token exchange — zero secrets in containers.

### Service Accounts
Service accounts are **pre-provisioned by Terraform** (`infra/terraform/manifests/service-account.yaml`) with the `azure.workload.identity/client-id` annotation. The Helm chart defaults to `serviceAccount.create: false` and references the Terraform-managed SA by name. Set `create: true` only for local dev clusters without Terraform.

### Health Probes
Every service must implement two endpoints:
- **`/health`** — Liveness probe. Returns 200 if the process is alive. Kubernetes restarts the pod on failure.
- **`/ready`** — Readiness probe. Returns 200 when the service can handle traffic (dependencies connected). Kubernetes removes the pod from service endpoints on failure.

### Resource Limits
Default values are conservative starting points. Profile each service under load and adjust:
- Requests: `100m` CPU, `128Mi` memory
- Limits: `500m` CPU, `512Mi` memory

### ConfigMap vs Secrets
- **ConfigMap**: Non-sensitive configuration (log levels, feature flags, environment name). Managed in `values.yaml` under `config:`.
- **Secrets**: Sensitive values (connection strings, API keys) should be stored in Azure Key Vault and surfaced via the CSI Secrets Store driver or Kubernetes secrets synced externally. The chart references existing secrets via `secretRefs:`.

## File Structure

```
infra/templates/
├── Dockerfile.template          # Multi-stage .NET 9 Dockerfile
├── README.md                    # This file
└── helm-base/
    ├── Chart.yaml               # Chart metadata
    ├── values.yaml              # Default configuration
    └── templates/
        ├── _helpers.tpl         # Name/label template helpers
        ├── configmap.yaml       # Non-secret configuration
        ├── deployment.yaml      # Pod spec with security + probes
        ├── hpa.yaml             # Horizontal Pod Autoscaler
        ├── NOTES.txt            # Post-install instructions
        ├── service.yaml         # ClusterIP service
        └── serviceaccount.yaml  # SA (create=false by default)
```
