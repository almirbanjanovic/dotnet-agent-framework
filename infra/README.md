# Infrastructure Setup

This folder contains all Terraform infrastructure-as-code and bootstrap scripts for the .NET Agent Framework.

> **You must complete the infrastructure setup before running any labs in `src/`.**

## Architecture

```
infra/
├── init-backend.ps1           # Bootstrap Terraform backend (PowerShell)
├── init-backend.sh            # Bootstrap Terraform backend (Bash)
└── terraform/
    ├── main.tf                # Root module — wires all child modules
    ├── variables.tf           # Root input variables (no defaults)
    ├── outputs.tf             # Root outputs (endpoints, keys, names)
    ├── providers.tf           # Provider versions and backend config
    ├── terraform.tfvars       # Your environment values (gitignored)
    ├── backend.hcl            # Remote state config (gitignored)
    │
    ├── acr/                   # Azure Container Registry
    │   └── v1/
    ├── aks/                   # AKS cluster + Log Analytics
    │   └── v1/
    ├── cosmosdb/              # Cosmos DB account, database, containers
    │   └── v1/
    ├── foundry/               # AI Services account + chat & embedding models
    │   └── v1/
    ├── identity/              # User-assigned managed identities
    │   └── v1/
    │
    └── rbac/
        ├── acr/               # AcrPull
        │   └── v1/
        ├── aks/               # AKS control plane Contributor
        │   └── v1/
        ├── cosmosdb/          # Cosmos DB Data Owner + Data Contributor
        │   └── v1/
        └── foundry/           # Cognitive Services OpenAI User
            └── v1/
```

## Prerequisites

- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) — run `az login` before any operations
- [Terraform >= 1.14.6](https://developer.hashicorp.com/terraform/install)
- A Microsoft Entra app registration with OIDC federated credentials (for CI/CD). See [configure OIDC for GitHub Actions](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-create-trust?pivots=identity-wif-apps-methods-azp#github-actions).
- The app registration's service principal needs **Contributor** on the target subscription:

  ```bash
  az role assignment create \
    --assignee <AZURE_CLIENT_ID> \
    --role "Contributor" \
    --scope "/subscriptions/<AZURE_SUBSCRIPTION_ID>"
  ```

## GitHub environment setup (CI/CD only)

### Repository secrets

| Secret                   | Description                                    |
|--------------------------|------------------------------------------------|
| `AZURE_CLIENT_ID`        | Service principal / app registration client ID |
| `AZURE_TENANT_ID`        | Azure AD tenant ID                             |
| `AZURE_SUBSCRIPTION_ID`  | Target Azure subscription ID                   |

### Environment variables

**Backend / bootstrap variables:**

| Variable                                | Example                      |
|-----------------------------------------|------------------------------|
| `RESOURCE_GROUP`                        | `rg-agentic-ai-centralus`   |
| `LOCATION`                              | `centralus`                  |
| `STORAGE_ACCOUNT`                       | `stagenticaicentralus`       |
| `STORAGE_ACCOUNT_SKU`                   | `Standard_LRS`               |
| `STORAGE_ACCOUNT_ENCRYPTION_SERVICES`   | `blob`                       |
| `STORAGE_ACCOUNT_MIN_TLS_VERSION`       | `TLS1_2`                     |
| `STORAGE_ACCOUNT_PUBLIC_NETWORK_ACCESS` | `Enabled`                    |
| `TERRAFORM_STATE_CONTAINER`             | `tfstate`                    |
| `TERRAFORM_STATE_BLOB`                  | `agentic-ai.tfstate`         |
| `TERRAFORM_WORKING_DIRECTORY`           | `infra/terraform`            |

**Terraform variables (mapped to `TF_VAR_` in workflows):**

| Variable                          | Example                        |
|-----------------------------------|--------------------------------|
| `TAGS`                            | `{}`                           |
| `ENVIRONMENT`                     | `agentic-ai`                   |
| `ITERATION`                       | `001`                          |
| `COGNITIVE_ACCOUNT_KIND`          | `AIServices`                   |
| `OAI_SKU_NAME`                    | `S0`                           |
| `OAI_DEPLOYMENT_SKU_NAME`        | `GlobalStandard`               |
| `OAI_DEPLOYMENT_MODEL_FORMAT`    | `OpenAI`                       |
| `OAI_DEPLOYMENT_MODEL_NAME`      | `gpt-4.1`                      |
| `OAI_DEPLOYMENT_MODEL_VERSION`   | `2025-04-14`                   |
| `OAI_VERSION_UPGRADE_OPTION`     | `NoAutoUpgrade`                |
| `CREATE_EMBEDDING_DEPLOYMENT`    | `true`                         |
| `EMBEDDING_MODEL_NAME`           | `text-embedding-ada-002`       |
| `EMBEDDING_MODEL_VERSION`        | `2`                            |
| `EMBEDDING_SKU_NAME`             | `Standard`                     |
| `EMBEDDING_CAPACITY`             | `10`                           |
| `COSMOS_PROJECT_NAME`            | `dotnetagent`                  |
| `COSMOS_ITERATION`               | `001`                          |
| `COSMOS_DATABASE_NAME`           | `contoso`                      |
| `COSMOS_AGENT_STATE_CONTAINER_NAME` | `workshop_agent_state_store` |
| `ACR_PROJECT_NAME`               | `dotnetagent`                  |
| `CREATE_ACR`                     | `true`                         |
| `ACR_SKU`                        | `Premium`                      |
| `EXISTING_ACR_NAME`              | (empty)                        |
| `AKS_KUBERNETES_VERSION`         | (empty for latest)             |
| `AKS_NODE_VM_SIZE`               | `Standard_D4s_v5`              |
| `AKS_NODE_COUNT`                 | `2`                            |
| `AKS_AUTO_SCALING_ENABLED`       | `true`                         |
| `AKS_NODE_MIN_COUNT`             | `1`                            |
| `AKS_NODE_MAX_COUNT`             | `5`                            |
| `AKS_OS_DISK_SIZE_GB`            | `64`                           |
| `AKS_LOG_RETENTION_DAYS`         | `30`                           |

## Step 1 — Create local config files

Create `terraform/backend.hcl` (gitignored):

```hcl
resource_group_name  = "rg-agentic-ai-centralus"
storage_account_name = "stagenticaicentralus"
container_name       = "tfstate"
key                  = "agentic-ai.tfstate"
```

Create `terraform/terraform.tfvars` (gitignored):

```hcl
tags                = {}
resource_group_name = "rg-agentic-ai-centralus"

environment = "agentic-ai"
location    = "centralus"
iteration   = "001"

# Foundry (AI Services)
cognitive_account_kind       = "AIServices"
oai_sku_name                 = "S0"
oai_deployment_sku_name      = "GlobalStandard"
oai_deployment_model_format  = "OpenAI"
oai_deployment_model_name    = "gpt-4.1"
oai_deployment_model_version = "2025-04-14"
oai_version_upgrade_option   = "NoAutoUpgrade"

create_embedding_deployment = true
embedding_model_name        = "text-embedding-ada-002"
embedding_model_version     = "2"
embedding_sku_name          = "Standard"
embedding_capacity          = 10

# Cosmos DB
cosmos_project_name              = "dotnetagent"
cosmos_iteration                 = "001"
cosmos_database_name             = "contoso"
cosmos_agent_state_container_name = "workshop_agent_state_store"

# ACR
acr_project_name  = "dotnetagent"
create_acr        = true
acr_sku           = "Premium"
existing_acr_name = ""

# AKS
aks_kubernetes_version   = null
aks_node_vm_size         = "Standard_D4s_v5"
aks_node_count           = 2
aks_auto_scaling_enabled = true
aks_node_min_count       = 1
aks_node_max_count       = 5
aks_os_disk_size_gb      = 64
aks_log_retention_days   = 30
```

Ensure `backend.hcl` values match your CI/CD environment variables:

| `backend.hcl` key       | Workflow variable           |
|--------------------------|-----------------------------|
| `resource_group_name`    | `RESOURCE_GROUP`            |
| `storage_account_name`   | `STORAGE_ACCOUNT`           |
| `container_name`         | `TERRAFORM_STATE_CONTAINER` |

## Step 2 — Bootstrap the Terraform remote backend

This only needs to run **once per environment**.

### Option A — Local script

From the `infra/` directory:

```bash
# Bash / WSL / macOS
chmod +x init-backend.sh
./init-backend.sh
```

```powershell
# PowerShell
./init-backend.ps1
```

The scripts read `backend.hcl` and `terraform.tfvars` from `terraform/`.

### Option B — CI/CD (GitHub Actions)

Run `.github/workflows/terraform-init-backend.yaml` via manual dispatch.

## Step 3 — Deploy infrastructure

### Option A — Local

From `infra/terraform/`:

```bash
# Ensure the backend storage account is reachable
az storage account update \
  --name stagenticaicentralus \
  --resource-group rg-agentic-ai-centralus \
  --public-network-access Enabled

terraform init -reconfigure -backend-config=backend.hcl
terraform validate
terraform plan -var-file="terraform.tfvars"
terraform apply -auto-approve -var-file="terraform.tfvars"
```

### Option B — CI/CD (GitHub Actions)

Run `.github/workflows/terraform-plan-approve-apply.yaml` via manual dispatch.

## Outputs

After `terraform apply`, the following outputs are displayed:

| Output                       | Description                          |
|------------------------------|--------------------------------------|
| `openai_endpoint`            | Azure OpenAI endpoint URL            |
| `openai_api_key`             | API key (dev/learning convenience)   |
| `openai_deployment_name`     | Chat model deployment name           |
| `embedding_deployment_name`  | Embedding model deployment name      |
| `cosmosdb_endpoint`          | Cosmos DB endpoint                   |
| `cosmosdb_account_name`      | Cosmos DB account name               |
| `cosmosdb_database_name`     | Cosmos DB database name              |
| `acr_name`                   | Container registry name              |
| `acr_login_server`           | ACR login server URL                 |
| `aks_cluster_name`           | AKS cluster name                     |
| `aks_oidc_issuer_url`        | OIDC issuer for workload identity    |
| `backend_identity_client_id` | Backend workload identity client ID  |
| `kubelet_identity_client_id` | Kubelet identity client ID           |

Use the `openai_endpoint`, `openai_deployment_name`, and `openai_api_key` values to configure `src/appsettings.json` for the labs.

> **Note:** API key output is for learning/dev convenience. Do not use this pattern in production.

## Module versioning

Each module lives under a `v1/` folder. To make breaking changes, create a `v2/` alongside and migrate callers at your own pace.

## Notes

- Provider versions are pinned with `~>` constraints in `providers.tf`.
- `terraform.tfvars`, `backend.hcl`, and `*.backend.hcl` are gitignored under `infra/.gitignore`.
- The bootstrap workflow disables storage public network access after setup; ensure your network can reach the storage account for local backend operations.
- The resource group is created by the bootstrap scripts / workflow, not by Terraform. The name is passed into `main.tf` via `resource_group_name`.
