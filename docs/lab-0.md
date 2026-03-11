# Lab 0 — Bootstrap

This lab performs the one-time setup required before any infrastructure can be deployed. Choose your deployment path — **terminal** or **GitHub Actions** — and follow the corresponding option throughout.

> **Do this once.** Everything in Lab 0 is a one-time operation. Once complete, you won't need to repeat it unless you're setting up a new subscription or repository.

## Prerequisites

### Accounts

- An **Azure subscription** where you have **Owner** or **Contributor + User Access Administrator** permissions
- A **GitHub account** with a repository for this project (GitHub Actions path)

### Tools

- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) — run `az login` before starting
- [Terraform >= 1.14.6](https://developer.hashicorp.com/terraform/install)
- [.NET SDK 9.0+](https://dotnet.microsoft.com/download)
- [GitHub CLI](https://cli.github.com/) — GitHub Actions path; run `gh auth login` before starting

## Bootstrap the Terraform remote state backend

This step creates the Azure Storage resources (resource group, storage account, blob container) that Terraform uses as its remote backend. All state is stored in Azure Blob Storage — local state is never used.

> **This only needs to run once per environment.**

### Option A — Terminal

From the `infra/` directory:

```powershell
# PowerShell
./init-backend.ps1
```

```bash
# Bash / WSL / macOS
chmod +x init-backend.sh
./init-backend.sh
```

The script auto-generates `terraform.tfvars` (with sensible defaults) and `backend.hcl` if they don't exist, then creates the Azure resources. Edit `terraform.tfvars` after generation to customize resource names, regions, or SKUs.

### Option B — GitHub Actions

**1. Configure CI/CD**

The `init-github` script creates an Entra app registration with OIDC federation, sets GitHub repository secrets, and configures all environment variables for the `dev` GitHub environment.

```powershell
# PowerShell
cd infra
./init-github.ps1
```

```bash
# Bash / WSL / macOS
cd infra
chmod +x init-github.sh
./init-github.sh
```

The script auto-detects your subscription, tenant, and GitHub repo. Override with flags:

```bash
./init-github.sh --subscription "12345678-..." --repo "myorg/myrepo" --env "dev"
```

If your Entra app already exists:

```bash
./init-github.sh --skip-entra --app-client-id "12345678-..."
```

The script is idempotent — it checks for existing resources and skips what's already created.

What the script does:

1. Creates an [Entra app registration](https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-register-app?tabs=certificate) + service principal
2. Adds an [OIDC federated credential](https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-create-trust?pivots=identity-wif-apps-methods-azp#github-actions) so GitHub Actions can authenticate without stored secrets
3. Grants **Contributor** role on the subscription
4. Sets 3 GitHub repository secrets (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`)
5. Creates the `dev` GitHub environment with all infrastructure variables

**2. Run the bootstrap workflow**

1. Go to **Actions → Terraform Init Remote Backend** in your GitHub repository
2. Click **Run workflow**, select the `dev` environment, and confirm
3. The workflow authenticates via OIDC, creates the resource group, storage account, and blob container, then disables public network access on the storage account

## Verification checklist

**All paths:**

- [ ] Terraform remote state storage account exists in Azure (resource group + storage account + blob container)
- [ ] `az login` is authenticated to the correct subscription

**Terminal:**

- [ ] `infra/terraform/terraform.tfvars` exists with your infrastructure configuration
- [ ] `infra/terraform/backend.hcl` exists with your remote state storage account details

**GitHub Actions:**

- [ ] App registration exists in Entra with a federated credential for your GitHub repo and the `dev` environment
- [ ] `gh secret list` shows `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`
- [ ] `gh variable list --env dev` shows all infrastructure variables

## What's next

Proceed to [Lab 1 — Infrastructure, Validation & Data Seeding](lab-1.md) to deploy the Azure infrastructure and seed your databases.
