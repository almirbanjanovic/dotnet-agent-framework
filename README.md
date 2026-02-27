# dotnet-agent-framework

.NET Agent Framework tutorials based on [Microsoft Learn](https://learn.microsoft.com/en-us/agent-framework/overview/?pivots=programming-language-csharp).

## Repository structure

```
infra/getting-started/
  terraform/                     → Terraform IaC

.github/workflows/
  terraform-init-backend.yaml    → backend bootstrap workflow

src/getting-started/
  01-first-agent/                → first runnable agent
  02-add-tools/                  → tool-enabled agents
  03-multi-turn-conversations/   → conversation state
  04-memory-and-persistence/     → memory patterns
  05-first-workflow/             → workflow fundamentals
  06-hosting-your-agent/         → hosting and deployment
```

## Prerequisites

- [.NET SDK 9.0+](https://dotnet.microsoft.com/download)
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) (`az login` completed)
- [Terraform >= 1.14.6](https://developer.hashicorp.com/terraform/install)

## GitHub environment setup

Before running any workflows, configure the following in your GitHub repository:

### Repository secrets

Set these under **Settings → Secrets and variables → Actions → Secrets**:

| Secret                   | Description                                      |
|--------------------------|--------------------------------------------------|
| `AZURE_CLIENT_ID`        | Service principal / app registration client ID   |
| `AZURE_TENANT_ID`        | Azure AD tenant ID                               |
| `AZURE_SUBSCRIPTION_ID`  | Target Azure subscription ID                     |

### Environment secrets

Set these under **Settings → Environments → `<environment>` → Secrets**:

| Secret                   | Description                                      |
|--------------------------|--------------------------------------------------|
| `TAGS`                   | Resource tags (e.g. `"environment=dev"`)          |

### Environment variables

Set these under **Settings → Environments → `<environment>` → Variables**:

| Variable                              | Description                                          | Example                          |
|---------------------------------------|------------------------------------------------------|----------------------------------|
| `RESOURCE_GROUP`                      | Resource group for the Terraform state backend       | `rg-tfstate-dev-centralus`       |
| `LOCATION`                            | Azure region                                         | `centralus`                      |
| `STORAGE_ACCOUNT`                     | Storage account name (globally unique, max 24 chars) | `sttfstatedevcentralus`          |
| `STORAGE_ACCOUNT_SKU`                 | Storage account SKU                                  | `Standard_LRS`                   |
| `STORAGE_ACCOUNT_ENCRYPTION_SERVICES` | Encryption services to enable                        | `blob`                           |
| `STORAGE_ACCOUNT_MIN_TLS_VERSION`     | Minimum TLS version                                  | `TLS1_2`                         |
| `STORAGE_ACCOUNT_PUBLIC_NETWORK_ACCESS` | Public network access during creation              | `Enabled`                        |
| `TERRAFORM_STATE_CONTAINER`           | Blob container name for state files                  | `tfstate`                        |

## Step 1 — Bootstrap the Terraform remote backend

Run the GitHub Actions workflow `.github/workflows/terraform-init-backend.yaml` (manual dispatch) to create the backend resource group, storage account, and state container in Azure.

This only needs to run **once per environment**.

## Step 2 — Create local backend config

Create `infra/getting-started/terraform/backend.hcl` (this file is gitignored):

```hcl
resource_group_name  = "<terraform-state-resource-group>"
storage_account_name = "<terraform-state-storage-account>"
container_name       = "tfstate"
key                  = "getting-started.tfstate"
```

Use the values that match your workflow environment variables:

| `backend.hcl` key       | Workflow variable              |
|--------------------------|--------------------------------|
| `resource_group_name`    | `RESOURCE_GROUP`               |
| `storage_account_name`   | `STORAGE_ACCOUNT`              |
| `container_name`         | `TERRAFORM_STATE_CONTAINER`    |

## Step 3 — Create local Terraform variables

Create `infra/getting-started/terraform/terraform.tfvars` (this file is gitignored):

```hcl
tags                = {}
resource_group_name = "<your-resource-group>"

base_name   = "getting-started"
environment = "dev"
location    = "centralus"

cognitive_account_kind       = "OpenAI"
oai_sku_name                 = "S0"
oai_deployment_sku_name      = "GlobalStandard"
oai_deployment_model_name    = "gpt-4.1"
oai_deployment_model_version = "2025-04-14"
```

## Step 4 — Deploy infrastructure

From `infra/getting-started/terraform/`:

```bash
terraform init -reconfigure -backend-config=backend.hcl
terraform validate
terraform plan -var-file="terraform.tfvars"
terraform apply -var-file="terraform.tfvars"
```

To tear down:

```bash
terraform destroy -var-file="terraform.tfvars"
```

## Step 5 — Run the first agent sample

From `src/getting-started/01-first-agent/`:

```bash
dotnet restore
dotnet run
```

Configuration is read from `appsettings.Development.json` or environment variables:

| Key                            | Description                     |
|--------------------------------|---------------------------------|
| `AZURE_OPENAI_ENDPOINT`       | Azure OpenAI resource endpoint  |
| `AZURE_OPENAI_DEPLOYMENT_NAME`| Model deployment name           |

## Local validation (no remote state)

For quick syntax/config checks without a backend:

```bash
cd infra/getting-started/terraform
terraform fmt -recursive
terraform init -backend=false
terraform validate
```

## Notes

- Provider versions are pinned with `~>` constraints in `infra/getting-started/terraform/providers.tf`.
- `terraform.tfvars`, `backend.hcl`, and `*.backend.hcl` are gitignored under `infra/.gitignore`.
- The bootstrap workflow disables storage public network access after setup; ensure your local network can reach the storage account for backend operations.
