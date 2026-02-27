# dotnet-agent-framework

.NET Agent Framework tutorials based on [Microsoft Learn](https://learn.microsoft.com/en-us/agent-framework/overview/?pivots=programming-language-csharp).

## Repository structure

```
.github/workflows/
  terraform-init-backend.yaml    → Terraform backend bootstrap workflow

infra/
  init-backend.ps1               → Bootstrap Terraform backend (PowerShell)
  init-backend.sh                → Bootstrap Terraform backend (Bash)
  getting-started/
    terraform/                   → Terraform IaC for getting-started

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
- A single Microsoft Entra app registration with an OIDC federated credential **per GitHub environment** (e.g. `getting-started`). Each federated credential should be scoped to its corresponding GitHub environment. See [configure OIDC for GitHub Actions](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-create-trust?pivots=identity-wif-apps-methods-azp#github-actions).
- The app registration's service principal must have **Contributor** role on the target subscription:

  ```bash
  az role assignment create \
    --assignee <AZURE_CLIENT_ID> \
    --role "Contributor" \
    --scope "/subscriptions/<AZURE_SUBSCRIPTION_ID>"
  ```

## GitHub environment setup

Before running any workflows, configure the following in your GitHub repository:

### Repository secrets

Set these under **Settings → Secrets and variables → Actions → Repository Secrets**:

| Secret                   | Description                                      |
|--------------------------|--------------------------------------------------|
| `AZURE_CLIENT_ID`        | Service principal / app registration client ID   |
| `AZURE_TENANT_ID`        | Azure AD tenant ID                               |
| `AZURE_SUBSCRIPTION_ID`  | Target Azure subscription ID                     |

### Environment secrets

Set these under **Settings → Environments → `<environment>` → Secrets**:

| Secret                   | Description                                      |
|--------------------------|--------------------------------------------------|
| `TAGS`                   | Resource tags (e.g. `"environment=getting-started"`) |

### Environment variables

Set these under **Settings → Environments → `<environment>` → Variables**:

| Variable                                | Description                                          | Example                            |
|-----------------------------------------|------------------------------------------------------|------------------------------------|
| `RESOURCE_GROUP`                        | Resource group for the Terraform state backend       | `rg-getting-started-centralus`     |
| `LOCATION`                              | Azure region                                         | `centralus`                        |
| `STORAGE_ACCOUNT`                       | Storage account name (globally unique, max 24 chars) | `stgettingstartedcentralu`         |
| `STORAGE_ACCOUNT_SKU`                   | Storage account SKU                                  | `Standard_LRS`                     |
| `STORAGE_ACCOUNT_ENCRYPTION_SERVICES`   | Encryption services to enable                        | `blob`                             |
| `STORAGE_ACCOUNT_MIN_TLS_VERSION`       | Minimum TLS version                                  | `TLS1_2`                           |
| `STORAGE_ACCOUNT_PUBLIC_NETWORK_ACCESS` | Public network access during creation                | `Enabled`                          |
| `TERRAFORM_STATE_CONTAINER`             | Blob container name for state files                  | `tfstate`                          |
| `TERRAFORM_STATE_BLOB`                  | Name for state file                                  | `<environment>.tfstate`            |

## Step 1 — Create local config files

Create `infra/<environment>/terraform/backend.hcl` (this file is gitignored):

```hcl
resource_group_name  = "<terraform-state-resource-group>"
storage_account_name = "<terraform-state-storage-account>"
container_name       = "tfstate"
key                  = "<environment>.tfstate"
```

Create `infra/<environment>/terraform/terraform.tfvars` (this file is gitignored):

```hcl
tags                = {}
resource_group_name = "<your-resource-group>"

base_name   = "getting-started"
environment = "getting-started"
location    = "centralus"

cognitive_account_kind       = "OpenAI"
oai_sku_name                 = "S0"
oai_deployment_sku_name      = "GlobalStandard"
oai_deployment_model_name    = "gpt-4.1"
oai_deployment_model_version = "2025-04-14"
```

If using CI/CD, ensure the `backend.hcl` values match your workflow environment variables:

| `backend.hcl` key       | Workflow variable              |
|--------------------------|--------------------------------|
| `resource_group_name`    | `RESOURCE_GROUP`               |
| `storage_account_name`   | `STORAGE_ACCOUNT`              |
| `container_name`         | `TERRAFORM_STATE_CONTAINER`    |

## Step 2 — Bootstrap the Terraform remote backend

This only needs to run **once per environment**. Choose one option:

**Option A — Local script:**

Ensure you are logged in (`az login`), then run **one** of the following from `infra/`:

```bash
# Bash / WSL / macOS
chmod +x init-backend.sh
./init-backend.sh
```

```powershell
# PowerShell
./init-backend.ps1
```

The scripts will present a menu to select which environment to bootstrap (currently `getting-started`). They then read `backend.hcl` and `terraform.tfvars` from the selected environment's `terraform/` directory — no duplicate config needed.

**Option B — CI/CD (GitHub Actions):**

Run the workflow `.github/workflows/terraform-init-backend.yaml` via manual dispatch. Requires the GitHub environment secrets and variables described above.

## Step 3 — Deploy infrastructure

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

## Step 4 — Run the first agent sample

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
