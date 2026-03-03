# dotnet-agent-framework

.NET Agent Framework tutorials based on [Microsoft Learn](https://learn.microsoft.com/en-us/agent-framework/overview/?pivots=programming-language-csharp).

## Repository structure

```
.github/workflows/
  terraform-init-backend.yaml    → Terraform backend bootstrap workflow

infra/
  init-backend.ps1               → Bootstrap Terraform backend (PowerShell)
  init-backend.sh                → Bootstrap Terraform backend (Bash)
  terraform/                     → Terraform IaC

src/
  simple-agent/                  → first runnable agent
```

## Prerequisites

- [.NET SDK 9.0+](https://dotnet.microsoft.com/download)
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) (`az login` completed)
- [Terraform >= 1.14.6](https://developer.hashicorp.com/terraform/install)
- A single Microsoft Entra app registration with an OIDC federated credential **per GitHub environment** (e.g. `agentic-ai`). Each federated credential should be scoped to its corresponding GitHub environment. See [configure OIDC for GitHub Actions](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-create-trust?pivots=identity-wif-apps-methods-azp#github-actions).
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
| `TAGS`                   | Resource tags (e.g. `"environment=agentic-ai"`) |

### Environment variables

Set these under **Settings → Environments → `<environment>` → Variables**:

| Variable                                | Description                                          | Example                            |
|-----------------------------------------|------------------------------------------------------|------------------------------------|
| `RESOURCE_GROUP`                        | Resource group for the Terraform state backend       | `rg-agentic-ai-centralus`          |
| `LOCATION`                              | Azure region                                         | `centralus`                        |
| `STORAGE_ACCOUNT`                       | Storage account name (globally unique, max 24 chars) | `stagenticaicentralus`             |
| `STORAGE_ACCOUNT_SKU`                   | Storage account SKU                                  | `Standard_LRS`                     |
| `STORAGE_ACCOUNT_ENCRYPTION_SERVICES`   | Encryption services to enable                        | `blob`                             |
| `STORAGE_ACCOUNT_MIN_TLS_VERSION`       | Minimum TLS version                                  | `TLS1_2`                           |
| `STORAGE_ACCOUNT_PUBLIC_NETWORK_ACCESS` | Public network access during creation                | `Enabled`                          |
| `TERRAFORM_STATE_CONTAINER`             | Blob container name for state files                  | `tfstate`                          |
| `TERRAFORM_STATE_BLOB`                  | Name for state file                                  | `<environment>.tfstate`            |
| `TERRAFORM_WORKING_DIRECTORY`           | Path to the Terraform files (relative to repo root)  | `infra/terraform`                  |

## Step 1 — Create local config files

Create `infra/terraform/backend.hcl` (this file is gitignored):

```hcl
resource_group_name  = "<terraform-state-resource-group>"
storage_account_name = "<terraform-state-storage-account>"
container_name       = "tfstate"
key                  = "<environment>.tfstate"
```

Create `infra/terraform/terraform.tfvars` (this file is gitignored):

```hcl
tags                = {}
resource_group_name = "<your-resource-group>"

environment = "agentic-ai"
location    = "centralus"

cognitive_account_kind       = "AIServices"
oai_sku_name                 = "S0"
oai_deployment_sku_name      = "GlobalStandard"
oai_deployment_model_format  = "OpenAI"
oai_deployment_model_name    = "gpt-4.1"
oai_deployment_model_version = "2025-04-14"
oai_version_upgrade_option   = "NoAutoUpgrade"
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

The scripts read `backend.hcl` and `terraform.tfvars` from `infra/terraform/`.

**Option B — CI/CD (GitHub Actions):**

Run the workflow `.github/workflows/terraform-init-backend.yaml` via manual dispatch. Requires the GitHub environment secrets and variables described above.

## Step 3 — Deploy infrastructure

**Option A — Local (with remote backend):**

From `infra/terraform/`, first ensure the backend storage account has public network access enabled:

```bash
az storage account update --name stagenticaicentralus --resource-group rg-agentic-ai-centralus --public-network-access Enabled
```

Then run:

```bash
terraform init -reconfigure -backend-config=backend.hcl
terraform validate
terraform plan -var-file="terraform.tfvars"
terraform apply -auto-approve -var-file="terraform.tfvars"
```

**Option B — CI/CD (GitHub Actions):**

Run the workflow `.github/workflows/terraform-plan-approve-apply.yaml` via manual dispatch. It will plan, wait for manual approval, then apply. Requires the GitHub environment secrets and variables described above.

## Step 4 — Run the first agent sample

First, create `src/appsettings.json` (this file is gitignored):

```json
{
  "AZURE_OPENAI_ENDPOINT": "<your-endpoint>",
  "AZURE_OPENAI_DEPLOYMENT_NAME": "oai-deployment-agentic-ai-centralus",
  "AZURE_OPENAI_API_KEY": "<your-api-key>"
}
```

You can find these values in the [Azure AI Foundry portal](https://ai.azure.com):

1. Open your project and go to **Models + endpoints**.
2. Select your deployment to see the **Target URI** (endpoint) and **Key** (API key).
3. The **Deployment name** is shown in the deployments list.

> **Note:** The Azure Portal's "Keys and Endpoint" blade may show a `.cognitiveservices.azure.com` endpoint for `AIServices` resources. Use the `.openai.azure.com` endpoint shown in Azure AI Foundry — that's what the `AzureOpenAIClient` SDK expects.

| Key                            | Description                    | Where to find it                                                 |
|--------------------------------|--------------------------------|------------------------------------------------------------------|
| `AZURE_OPENAI_ENDPOINT`        | Azure OpenAI resource endpoint | Foundry → Overview → Azure OpenAI → Azure OpenAI endpoint        |
| `AZURE_OPENAI_DEPLOYMENT_NAME` | Model deployment name          | Derived from `main.tf`: `oai-deployment-{environment}-{location}`|
| `AZURE_OPENAI_API_KEY`         | API key for authentication     | Foundry → Overview → API Key                                     |

Then, from `src/simple-agent/`:

```bash
dotnet restore
dotnet run
```

The `appsettings.json` is shared across all samples under `src/`.

## Notes

- Provider versions are pinned with `~>` constraints in `infra/terraform/providers.tf`.
- `terraform.tfvars`, `backend.hcl`, and `*.backend.hcl` are gitignored under `infra/.gitignore`.
- The bootstrap workflow disables storage public network access after setup; ensure your local network can reach the storage account for backend operations.
