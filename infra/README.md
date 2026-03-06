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
    └── modules/
        ├── acr/               # Azure Container Registry
        │   └── v1/
        ├── aks/               # AKS cluster + Log Analytics
        │   └── v1/
        ├── cosmosdb/          # Cosmos DB account, database, containers
        │   └── v1/
        ├── foundry/           # AI Services account + chat & embedding models
        │   └── v1/
        ├── identity/          # User-assigned managed identities
        │   └── v1/
        ├── keyvault/          # Azure Key Vault
        │   └── v1/
        ├── keyvault-secrets/  # Key Vault secret writer
        │   └── v1/
        │
        └── rbac/
            ├── acr/           # AcrPull
            │   └── v1/
            ├── aks/           # AKS control plane Contributor
            │   └── v1/
            ├── cosmosdb/      # Cosmos DB Data Owner + Data Contributor
            │   └── v1/
            ├── foundry/       # Cognitive Services OpenAI User
            │   └── v1/
            └── keyvault/      # Key Vault Secrets Officer + User
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

Set all of the following under **Settings → Environments → `<environment>` → Variables** in your GitHub repository.

**Backend / bootstrap:**

| Variable                                | Example                      | Description |
|-----------------------------------------|------------------------------|-------------|
| `RESOURCE_GROUP`                        | `rg-agentic-ai-centralus`   | Resource group for Terraform state storage and deployed resources |
| `LOCATION`                              | `centralus`                  | Azure region for all resources |
| `STORAGE_ACCOUNT`                       | `stagenticaicentralus`       | Globally unique name for the storage account holding Terraform state |
| `STORAGE_ACCOUNT_SKU`                   | `Standard_LRS`               | Replication tier for the state storage account |
| `STORAGE_ACCOUNT_ENCRYPTION_SERVICES`   | `blob`                       | Which storage services to encrypt at rest |
| `STORAGE_ACCOUNT_MIN_TLS_VERSION`       | `TLS1_2`                     | Minimum TLS version for storage account connections |
| `STORAGE_ACCOUNT_PUBLIC_NETWORK_ACCESS` | `Enabled`                    | Public access during bootstrap (disabled after setup by workflow) |
| `TERRAFORM_STATE_CONTAINER`             | `tfstate`                    | Blob container name for the `.tfstate` file |
| `TERRAFORM_STATE_BLOB`                  | `agentic-ai.tfstate`         | Name of the state file blob |
| `TERRAFORM_WORKING_DIRECTORY`           | `infra/terraform`            | Relative path to the Terraform root module |

**Infrastructure configuration (mapped to `TF_VAR_` in workflows):**

| Variable                          | Example                        | Description |
|-----------------------------------|--------------------------------|-------------|
| `TAGS`                            | `{}`                           | Resource tags applied to all deployed resources |
| `ENVIRONMENT`                     | `agentic-ai`                   | Logical environment name used in resource naming |
| `ITERATION`                       | `001`                          | Counter to avoid soft-delete naming collisions across teardown/redeploy cycles |
| `COGNITIVE_ACCOUNT_KIND`          | `AIServices`                   | Azure AI Services account type (`AIServices` or `OpenAI`) |
| `OAI_SKU_NAME`                    | `S0`                           | Pricing tier for the AI Services account |
| `OAI_DEPLOYMENT_SKU_NAME`        | `GlobalStandard`               | SKU for the chat model deployment (affects throughput and availability) |
| `OAI_DEPLOYMENT_MODEL_FORMAT`    | `OpenAI`                       | Model format identifier |
| `OAI_DEPLOYMENT_MODEL_NAME`      | `gpt-4.1`                      | Chat model to deploy |
| `OAI_DEPLOYMENT_MODEL_VERSION`   | `2025-04-14`                   | Specific model version to pin |
| `OAI_VERSION_UPGRADE_OPTION`     | `NoAutoUpgrade`                | Prevents Azure from auto-upgrading the model version |
| `CREATE_EMBEDDING_DEPLOYMENT`    | `true`                         | Whether to deploy an embedding model alongside the chat model |
| `EMBEDDING_MODEL_NAME`           | `text-embedding-ada-002`       | Embedding model to deploy (used for vector search) |
| `EMBEDDING_MODEL_VERSION`        | `2`                            | Embedding model version |
| `EMBEDDING_SKU_NAME`             | `Standard`                     | SKU for the embedding deployment |
| `EMBEDDING_CAPACITY`             | `10`                           | Throughput capacity (TPM in thousands) for the embedding deployment |
| `COSMOS_PROJECT_NAME`            | `dotnetagent`                  | Project name prefix for Cosmos DB resource naming |
| `COSMOS_ITERATION`               | `001`                          | Iteration counter for Cosmos DB (separate from global to allow independent cycling) |
| `COSMOS_OPERATIONAL_DATABASE_NAME` | `contoso`                    | Database name for the operational (CRM) Cosmos DB account |
| `COSMOS_KNOWLEDGE_DATABASE_NAME` | `knowledge`                    | Database name for the knowledge (RAG) Cosmos DB account |
| `COSMOS_AGENTS_DATABASE_NAME`    | `agents`                       | Database name for the agents (state) Cosmos DB account |
| `COSMOS_AGENT_STATE_CONTAINER_NAME` | `workshop_agent_state_store` | Cosmos DB container for persisting agent state across sessions |
| `ACR_PROJECT_NAME`               | `dotnetagent`                  | Project name prefix for ACR resource naming |
| `CREATE_ACR`                     | `true`                         | Set to `false` to reference an existing ACR instead of creating one |
| `ACR_SKU`                        | `Premium`                      | ACR tier — Premium required for geo-replication and network rules |
| `EXISTING_ACR_NAME`              | (empty)                        | Name of existing ACR (only used when `CREATE_ACR` is `false`) |
| `AKS_KUBERNETES_VERSION`         | (empty for latest)             | Kubernetes version to deploy; leave empty to use the latest stable |
| `AKS_NODE_VM_SIZE`               | `Standard_D4s_v5`              | VM size for AKS worker nodes |
| `AKS_NODE_COUNT`                 | `2`                            | Initial number of nodes (used when auto-scaling is disabled) |
| `AKS_AUTO_SCALING_ENABLED`       | `true`                         | Enable cluster auto-scaler to scale nodes based on workload demand |
| `AKS_NODE_MIN_COUNT`             | `1`                            | Minimum node count when auto-scaling is enabled |
| `AKS_NODE_MAX_COUNT`             | `5`                            | Maximum node count when auto-scaling is enabled |
| `AKS_OS_DISK_SIZE_GB`            | `64`                           | OS disk size per node in GB |
| `AKS_LOG_RETENTION_DAYS`         | `30`                           | How long to retain logs in the Log Analytics workspace |

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

# Cosmos DB (3 accounts: operational, knowledge, agents)
cosmos_project_name               = "dotnetagent"
cosmos_iteration                  = "001"
cosmos_operational_database_name  = "contoso"
cosmos_knowledge_database_name    = "knowledge"
cosmos_agents_database_name       = "agents"
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
| `cosmosdb_operational_endpoint` | Operational Cosmos DB endpoint    |
| `cosmosdb_operational_account_name` | Operational Cosmos DB account name |
| `cosmosdb_operational_database_name` | Operational database name       |
| `cosmosdb_knowledge_endpoint` | Knowledge Cosmos DB endpoint       |
| `cosmosdb_knowledge_account_name` | Knowledge Cosmos DB account name |
| `cosmosdb_knowledge_database_name` | Knowledge database name         |
| `cosmosdb_agents_endpoint`   | Agents Cosmos DB endpoint            |
| `cosmosdb_agents_account_name` | Agents Cosmos DB account name      |
| `cosmosdb_agents_database_name` | Agents database name               |
| `acr_name`                   | Container registry name              |
| `acr_login_server`           | ACR login server URL                 |
| `aks_cluster_name`           | AKS cluster name                     |
| `aks_oidc_issuer_url`        | OIDC issuer for workload identity    |
| `backend_identity_client_id` | Backend workload identity client ID  |
| `kubelet_identity_client_id` | Kubelet identity client ID           |
| `keyvault_name`              | Key Vault name                       |
| `keyvault_uri`               | Key Vault URI (for config-sync tool) |

All secrets (OpenAI endpoint/key, Cosmos DB endpoint/key, deployment names) are automatically written to Key Vault by Terraform. Use the **config-sync** tool to pull them into `src/appsettings.json`:

```bash
cd src/config-sync
dotnet run -- $(terraform output -raw keyvault_uri)
```

See [src/README.md](../src/README.md) for details.

> **Note:** API key and Cosmos DB key outputs are for learning/dev convenience. Do not expose sensitive values via Terraform outputs in production.

## Module versioning

Each module lives under a `v1/` folder. When a breaking change is needed, create a `v2/` alongside and migrate callers at your own pace. The old version stays in place until all references are updated.

**When to create a new version:**

- **Provider breaking changes** — A new `azurerm` or `azapi` provider version deprecates or renames a resource/attribute (e.g., `azurerm_kubernetes_cluster` restructures its `identity` block).
- **Terraform version upgrades** — A new Terraform version introduces syntax changes or removes deprecated features that affect module internals.
- **Resource API changes** — Azure retires an API version or changes required properties (e.g., Cosmos DB adds a mandatory field).
- **Structural redesigns** — You want to change the module interface (add/remove/rename variables or outputs) in a way that would break existing callers.
- **Security or compliance** — A new security requirement changes how resources must be configured (e.g., mandatory private endpoints, encryption settings).

**When NOT to create a new version:**

- Adding optional variables with defaults (backward-compatible).
- Bug fixes that don't change the module interface.
- Adding new outputs.

## Notes

- Provider versions are pinned with `~>` constraints in `providers.tf`.
- `terraform.tfvars`, `backend.hcl`, and `*.backend.hcl` are gitignored under `infra/.gitignore`.
- The bootstrap workflow disables storage public network access after setup; ensure your network can reach the storage account for local backend operations.
- The resource group is created by the bootstrap scripts / workflow, not by Terraform. The name is passed into `main.tf` via `resource_group_name`.
