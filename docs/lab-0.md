# Lab 0 — Bootstrap

This lab performs the one-time setup required before any infrastructure can be deployed. A single script handles everything: Azure backend resources, Entra app registration, and GitHub CI/CD configuration.

> **Do this once.** Everything in Lab 0 is a one-time operation. Once complete, you won't need to repeat it unless you're setting up a new subscription or repository.

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
|:-----:|-------------|
| **1** | Generates `terraform.tfvars` and `backend.hcl` (if they don't exist) — edit `terraform.tfvars` after to customize |
| **2** | Creates Azure resource group, storage account, and blob container for Terraform remote state |
| **3** | Creates Entra app registration with OIDC federation for GitHub Actions, grants Contributor role |
| **4** | Sets GitHub repository secrets and environment variables (reads values from `terraform.tfvars`) |
| **5** | Disables public network access on the state storage account |

The script is idempotent — it checks for existing resources and skips what's already created.

Override defaults with flags:

```bash
./init.sh --subscription "12345678-..." --repo "myorg/myrepo" --env "dev"
```

If your Entra app already exists:

```bash
./init.sh --skip-entra --app-client-id "12345678-..."
```

## Verification checklist

- [ ] Terraform remote state storage account exists in Azure
- [ ] `infra/terraform/terraform.tfvars` exists with your infrastructure configuration
- [ ] `infra/terraform/backend.hcl` exists with your remote state storage account details
- [ ] App registration exists in Entra with a federated credential for your GitHub repo
- [ ] `gh secret list` shows `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`
- [ ] `gh variable list --env dev` shows all infrastructure variables

## What's next

Proceed to [Lab 1 — Infrastructure, Validation & Data Seeding](lab-1.md) to deploy the Azure infrastructure and seed your databases.
