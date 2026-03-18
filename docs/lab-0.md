# Lab 0 — Bootstrap

This lab performs the one-time setup required before any infrastructure can be deployed. A single script handles everything: Azure backend resources, Entra app registration, and GitHub CI/CD configuration.

> **Do this once.** Everything in Lab 0 is a one-time operation. Once complete, you won't need to repeat it unless you're setting up a new subscription, repository or environment. The scripts below are **idempotent**.

## Prerequisites

### Accounts

- An **Azure subscription** where you have **Owner** or **Contributor + User Access Administrator** permissions
- A **GitHub account** with a repository for this project

### Tools

- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) — run `az login` before starting
- [GitHub CLI](https://cli.github.com/) — run `gh auth login` before starting
- [Terraform >= 1.14.6](https://developer.hashicorp.com/terraform/install)
- [.NET SDK 9.0+](https://dotnet.microsoft.com/download)

## Run the bootstrap script

From the `infra/` directory:

```powershell
# PowerShell
./init.ps1
```

```bash
# Bash / WSL / macOS
chmod +x init.sh
./init.sh
```

The script performs 5 phases in order:

| Phase | What it does |
| :-----: | ------------- |
| **1** | Authenticates to Azure (pick subscription) and GitHub (detect/create repo), selects environment |
| **2** | Creates Entra app registration with service principal and OIDC federated credential for GitHub Actions |
| **3** | Creates GitHub environment, sets repository secrets (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`) and environment variables |
| **4** | Creates Azure resource group, storage account, blob container for Terraform remote state, grants Contributor RBAC, then locks down public access |
| **5** | Generates `terraform.tfvars` and `backend.hcl` configuration files |

### Why `terraform.tfvars` and `backend.hcl` are gitignored

These files contain environment-specific values (resource names, backend storage details) that could expose your Azure topology. They are generated locally by the init script and excluded from source control — a standard Terraform security best practice.

Between each phase, the script shows a summary and previews what the next phase will do before continuing.

The script is idempotent — it checks for existing resources and skips what's already created.

## Verification checklist

- [ ] Terraform remote state storage account exists in Azure
- [ ] `infra/terraform/terraform.tfvars` exists with your infrastructure configuration
- [ ] `infra/terraform/backend.hcl` exists with your remote state storage account details
- [ ] App registration exists in Entra with a federated credential for your GitHub repo
- [ ] `gh secret list` shows `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`
- [ ] `gh variable list --env dev` shows all infrastructure variables

## What's next

Proceed to [Lab 1 — Infrastructure, Validation & Data Seeding](lab-1.md) to deploy the Azure infrastructure and seed your databases.
