# dotnet-agent-framework

.NET Agent Framework tutorials based on [Microsoft Learn](https://learn.microsoft.com/en-us/agent-framework/overview/?pivots=programming-language-csharp).

## Repository structure

```
infra/
  init-backend.ps1               → Bootstrap Terraform backend (PowerShell)
  init-backend.sh                → Bootstrap Terraform backend (Bash)
  terraform/                     → Terraform IaC (modular, versioned)

src/
  simple-agent/                  → Lab 1: first runnable agent
```

## Prerequisites

- [.NET SDK 9.0+](https://dotnet.microsoft.com/download)
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) (`az login` completed)
- [Terraform >= 1.14.6](https://developer.hashicorp.com/terraform/install)

## Getting started

### 1. Deploy infrastructure

Infrastructure must be provisioned before running any labs. Follow the full setup instructions in **[infra/README.md](infra/README.md)** — this covers backend bootstrap, Terraform configuration, and deployment.

### 2. Configure app settings

After infrastructure is deployed, create `src/appsettings.json` (this file is gitignored):

```json
{
  "AZURE_OPENAI_ENDPOINT": "<your-endpoint>",
  "AZURE_OPENAI_DEPLOYMENT_NAME": "<your-deployment-name>",
  "AZURE_OPENAI_API_KEY": "<your-api-key>"
}
```

These values are shown in Terraform outputs after `terraform apply`, or you can find them in the [Azure AI Foundry portal](https://ai.azure.com) under **Models + endpoints**.

| Key                            | Description                    | Source                                        |
|--------------------------------|--------------------------------|-----------------------------------------------|
| `AZURE_OPENAI_ENDPOINT`        | Azure OpenAI endpoint          | `terraform output openai_endpoint`            |
| `AZURE_OPENAI_DEPLOYMENT_NAME` | Model deployment name          | `terraform output openai_deployment_name`     |
| `AZURE_OPENAI_API_KEY`         | API key for authentication     | `terraform output openai_api_key`             |

> **Note:** Use the `.openai.azure.com` endpoint (shown in AI Foundry), not the `.cognitiveservices.azure.com` endpoint from the Azure Portal.

### 3. Run a lab

From the lab directory (e.g., `src/simple-agent/`):

```bash
dotnet restore
dotnet run
```

The `appsettings.json` is shared across all labs under `src/`.

## Notes

- Provider versions are pinned in `infra/terraform/providers.tf`.
- `terraform.tfvars`, `backend.hcl`, and `appsettings.json` are gitignored.
