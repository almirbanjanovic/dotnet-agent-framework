# Infrastructure Reference

This folder contains all Terraform infrastructure-as-code and bootstrap scripts for the .NET Agent Framework.

> **For step-by-step deployment instructions, see [Lab 0 — Bootstrap](../docs/lab-0.md) and [Lab 1 — Infrastructure, Validation & Data Seeding](../docs/lab-1.md).**

## Architecture

```text
infra/
├── init.ps1                   # One-time bootstrap: backend + Entra + GitHub CI/CD (PowerShell)
├── init.sh                    # One-time bootstrap: backend + Entra + GitHub CI/CD (Bash)
├── deploy.ps1                 # Deploy infrastructure + seed data (PowerShell)
├── deploy.sh                  # Deploy infrastructure + seed data (Bash)
└── terraform/
    ├── main.tf                # Root module — wires all child modules
    ├── variables.tf           # Root input variables (no defaults)
    ├── outputs.tf             # Root outputs (endpoints, keys, names)
    ├── providers.tf           # Provider versions and backend config
    ├── dev.tfvars             # Your environment values (gitignored, name matches env)
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
        ├── search/            # Azure AI Search service (Standard tier, semantic ranker)
        │   └── v1/
        ├── storage/           # Azure Storage Account + blob containers (control plane)
        │   └── v1/
        ├── storage-uploads/   # Blob file uploads (data plane, separate ordering)
        │   └── v1/
        │
        └── rbac/
            ├── acr/           # AcrPull
            │   └── v1/
            ├── aks/           # AKS control plane Contributor
            │   └── v1/
            ├── cosmosdb/      # Cosmos DB Data Owner
            │   └── v1/
            ├── foundry/       # Cognitive Services OpenAI User
            │   └── v1/
            ├── keyvault/      # Key Vault Secrets Officer + User
            │   └── v1/
            └── storage/       # Storage Blob Data Reader
                └── v1/
```

## Prerequisites

- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) — run `az login` before any operations
- [Terraform >= 1.14.6](https://developer.hashicorp.com/terraform/install)

## Outputs

After `terraform apply`, the following outputs are displayed:

| Output | Description |
| --- | --- |
| `openai_endpoint` | Azure OpenAI endpoint URL |
| `openai_api_key` | API key (dev/learning convenience) |
| `openai_deployment_name` | Chat model deployment name |
| `embedding_deployment_name` | Embedding model deployment name |
| `cosmosdb_crm_endpoint` | CRM Cosmos DB endpoint |
| `cosmosdb_crm_account_name` | CRM Cosmos DB account name |
| `cosmosdb_crm_database_name` | CRM database name |
| `cosmosdb_agents_endpoint` | Agents Cosmos DB endpoint |
| `cosmosdb_agents_account_name` | Agents Cosmos DB account name |
| `cosmosdb_agents_database_name` | Agents database name |
| `search_endpoint` | Azure AI Search endpoint URL |
| `search_name` | Azure AI Search service name |
| `search_index_name` | AI Search index name |
| `acr_name` | Container registry name |
| `acr_login_server` | ACR login server URL |
| `aks_cluster_name` | AKS cluster name |
| `aks_oidc_issuer_url` | OIDC issuer for workload identity |
| `aks_fqdn` | AKS cluster FQDN |
| `agc_frontend_fqdn` | AGC frontend FQDN (for Ingress + Entra redirect URI) |
| `bff_identity_client_id` | BFF workload identity client ID |
| `crm_api_identity_client_id` | CRM API workload identity client ID |
| `crm_mcp_identity_client_id` | CRM MCP workload identity client ID |
| `know_mcp_identity_client_id` | Knowledge MCP identity client ID |
| `crm_agent_identity_client_id` | CRM Agent identity client ID |
| `prod_agent_identity_client_id` | Product Agent identity client ID |
| `orch_agent_identity_client_id` | Orchestrator Agent identity client ID |
| `kubelet_identity_client_id` | Kubelet identity client ID |
| `keyvault_name` | Key Vault name |
| `keyvault_uri` | Key Vault URI (for config-sync tool) |
| `storage_images_account_name` | Product images storage account name |
| `storage_images_blob_endpoint` | Product images blob endpoint |
| `storage_images_container_name` | Product images container name |
| `entra_bff_client_id` | Entra app registration client ID |
| `entra_tenant_id` | Entra tenant ID |
| `entra_domain` | Entra default verified domain |
| `entra_test_user_upns` | Test user login emails |
| `tls_cert_secret_id` | TLS cert Key Vault secret ID |

All secrets (OpenAI endpoint/key, Cosmos DB endpoint/key, deployment names) are automatically written to Key Vault by Terraform. See [Lab 1 Step 2](../docs/lab-1.md#step-2--configure-app-settings) for pulling them into local config.

> **Note:** API key and Cosmos DB key outputs are for learning/dev convenience. Do not expose sensitive values via Terraform outputs in production.

## Recent infrastructure additions

The following resources were added to support the full application architecture (8 containers in AKS):

| Addition | Details |
| --- | --- |
| **5 managed identities** | `id-bff`, `id-crm-api`, `id-crm-mcp`, `id-know-mcp`, `id-kubelet` |
| **3 agent identities** | `Contoso CRM Agent`, `Contoso Product Agent`, `Contoso Orchestrator Agent` (Entra Agent ID blueprints + instances) |
| **`agent-identity/v1/`** | Agent Identity Blueprints + Agent Identity service principals + FIC for AKS workload identity |
| **`rbac/search/v1/`** | Search Index Data Reader for `id-know-mcp` |
| **`workload-identity/v1/`** | Federated credentials binding each identity to AKS OIDC issuer + K8s service accounts |
| **`entra/v1/`** | Entra app registration, Customer app role, 5 customer test users with random passwords, role assignments |
| **`tls-cert/v1/`** | Self-signed TLS certificate in Key Vault for AGC TLS termination |
| **`vnet/v1/`** | Virtual Network with 3 subnets (AKS system, AKS workload, AGC) |
| **`agc/v1/`** | App Gateway for Containers + Frontend + Subnet Association |
| **Knowledge Source (`knowledge-source/v1/`)** | Creates AI Search Knowledge Source via REST API — auto-generates index, data source, skillset, indexer |
| **Cosmos DB `conversations`** | New container (partition key: `/sessionId`) for BFF-owned chat history |
| **Key Vault secrets** | Identity client IDs, Entra app credentials, test user passwords, AKS hostname |

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
- `*.tfvars`, `backend.hcl`, and `*.backend.hcl` are gitignored under `infra/.gitignore`.
- The bootstrap scripts and workflow disable storage public network access after setup; ensure your network can reach the storage account when running Terraform locally.
- The resource group is created by the bootstrap scripts / workflow, not by Terraform. The name is passed into `main.tf` via `resource_group_name`.

## Deploy script safety features

| Feature | Description |
| --- | --- |
| **Soft-delete purge** | Pre-flight purge of soft-deleted Cognitive Services accounts and Key Vaults. KV purges use `--no-wait` to avoid blocking. |
| **Entra user import** | Imports existing Entra test users into Terraform state before plan/apply to prevent recreation conflicts. |
| **CAE token retry** | If Entra operations fail due to a `TokenCreatedWithOutdatedPolicies` challenge, the script clears cached tokens and re-authenticates interactively. |
| **Policy diagnostic** | On `terraform apply` failure, lists all deny-effect Azure Policy assignments (resolving parameterized effects through assignment overrides and definition defaults). |
| **Entra-to-Customer linking** | Entra user IDs are linked to Customer documents in Cosmos DB using the seed-data tool's `ENTRA_MAPPING` mode. |
| **State storage lock** | Public access on the Terraform state storage account is disabled after every run. CI/CD uses `if: always()` to guarantee this even on failure. |
