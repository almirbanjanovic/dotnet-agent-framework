# dotnet-agent-framework

.NET Agent Framework tutorials based on [Microsoft Learn](https://learn.microsoft.com/en-us/agent-framework/overview/?pivots=programming-language-csharp).

## Repository structure

```
.github/workflows/                → CI/CD (plan, apply, backend bootstrap)

data/
  README.md                       → Data architecture and seeding guide
  contoso-crm/                    → Simulated CRM export (CSV)
  contoso-sharepoint/             → Simulated SharePoint docs (TXT + PDF)

infra/
  README.md                       → Infrastructure setup guide
  init-backend.ps1                → Bootstrap Terraform backend (PowerShell)
  init-backend.sh                 → Bootstrap Terraform backend (Bash)
  terraform/                      → Terraform IaC (modular, versioned)

src/
  README.md                       → Lab setup and run guide
  appsettings.json                → Shared app settings (gitignored, populated by config-sync)
  config-sync/                    → Tool: pulls Key Vault secrets into appsettings.json
  simple-agent/                   → Lab 1: validate infrastructure setup
  seed-data/                      → Lab 2: seed Cosmos DB with CRM + vectorized docs (RAG)
```

## Prerequisites

- [.NET SDK 9.0+](https://dotnet.microsoft.com/download)
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) (`az login` completed)
- [Terraform >= 1.14.6](https://developer.hashicorp.com/terraform/install)

## Getting started

### 1. Deploy infrastructure

Infrastructure must be provisioned before running any labs. Follow the full setup instructions in **[infra/README.md](infra/README.md)** — this covers backend bootstrap, Terraform configuration, and deployment.

### 2. Configure agentic app settings

After infrastructure is deployed, sync secrets from Key Vault to your local config. Follow the instructions in **[src/README.md](src/README.md)**.

## Notes

- Provider versions are pinned in `infra/terraform/providers.tf`.
- `terraform.tfvars`, `backend.hcl`, and `appsettings.json` are gitignored.
