# Lab 0 — Bootstrap

> ## 📍 Lab 0 has two tracks
>
> Pick one based on what you're trying to do. Both tracks share no state and you can switch later.
>
> | | **Local Track** *(Foundry only)* | **Full Azure Track** *(production-shaped)* |
> |---|---|---|
> | Time | ~5 min (mostly tool installs) | ~10 min |
> | Sets up | `az login` + tool checks. Lab 1's `setup-local` then provisions one Foundry account **and** an Entra SPA app registration with test users in your tenant | Terraform remote-state backend, Entra app reg, GitHub OIDC, env vars |
> | User auth | Microsoft Entra ID via MSAL (real sign-in) | Microsoft Entra ID via MSAL (real sign-in) |
> | Why | All you need is a Foundry endpoint your laptop can hit + a tenant that lets you create app regs and test users | Required for AKS / CI/CD / multi-environment work |
> | Continues to | [Lab 1 — Local Track](lab-1.md#local-track--foundry-only-deployment) | [Lab 1 — Full Azure Track](lab-1.md#full-azure-track--full-infrastructure--seeding) |
>
> Jump to: [Local Track](#local-track) · [Full Azure Track](#full-azure-track)

---

## Local Track

Lab 0 on the Local Track is just a sanity check. There is no remote-state backend, no GitHub OIDC. Lab 1's `setup-local` script provisions a per-developer Foundry account, an Entra SPA app registration with localhost callbacks, and a small set of test users in your tenant — both tracks use the same MSAL sign-in flow.

### Prerequisites

- An **Azure subscription** where you can create a resource group + Azure AI Services account (Contributor on a subscription, or Owner on a resource group, is sufficient).
- An **Entra tenant** where you can create a SPA app registration **and** test users. In practice this means:
  - **Application Developer** (or higher) — to create the BFF app registration
  - **User Administrator** (or higher)    — to create the 8 seeded test users (Emma, James, Sarah, David, Lisa, Mike, Anna, Tom)

  Most M365 developer tenants grant both by default. Enterprise tenants typically don't — if you don't have these roles, [create a free M365 dev tenant](https://learn.microsoft.com/microsoft-365/developer/microsoft-365-developer-program) and run the Local Track there.
- Tools:
  - [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)
  - [Terraform >= 1.14.7](https://developer.hashicorp.com/terraform/install)
  - [.NET SDK 9.0+](https://dotnet.microsoft.com/download)

### Run

```bash
az login
az account set --subscription "<your-subscription-id>"   # only if you have more than one
```

### Verification checklist

- [ ] `az account show` returns the subscription you intend to deploy into
- [ ] `az ad signed-in-user show` returns your tenant identity (confirms the Entra side of `az login`)
- [ ] `dotnet --version` ≥ 9.0
- [ ] `terraform --version` ≥ 1.14.7

### What's next

Proceed to [Lab 1 — Local Track](lab-1.md#local-track--foundry-only-deployment). The `setup-local` script there provisions Foundry, creates your local SPA app registration + 8 test users, and writes their UPNs and freshly-generated passwords to **`local-dev-credentials.txt`** at the repo root (gitignored, rewritten on every run).

---

## Full Azure Track

This track performs the one-time setup required before any infrastructure can be deployed. A single script handles everything: Azure backend resources, Entra app registration, and GitHub CI/CD configuration.

> **Do this once.** Everything in Lab 0 (Full Track) is a one-time operation. Once complete, you won't need to repeat it unless you're setting up a new subscription, repository or environment. The scripts below are **idempotent**.

### Prerequisites

#### Accounts

- An **Azure subscription** where you have **Owner** or **Contributor + User Access Administrator** permissions
- A **GitHub account** with a repository for this project

#### Tools

- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) — run `az login` before starting
- [GitHub CLI](https://cli.github.com/) — run `gh auth login` before starting
- [Terraform >= 1.14.7](https://developer.hashicorp.com/terraform/install)
- [.NET SDK 9.0+](https://dotnet.microsoft.com/download)

### Run the bootstrap script

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
| **5** | Generates `dev.tfvars` (or `{env}.tfvars`), `backend.hcl`, and `deployments/{env}-{region}.env` configuration files |

#### Why `*.tfvars` and `backend.hcl` are gitignored

These files contain environment-specific values (resource names, backend storage details) that could expose your Azure topology. The init script generates `{env}.tfvars` (e.g., `dev.tfvars`) and `backend.hcl` locally — both are excluded from source control via `.gitignore`. This is a standard Terraform security best practice.

Between each phase, the script shows a summary and previews what the next phase will do before continuing.

The script is idempotent — it checks for existing resources and skips what's already created.

### Verification checklist

- [ ] Terraform remote state storage account exists in Azure
- [ ] `infra/terraform/dev.tfvars` (or `{env}.tfvars`) exists with your infrastructure configuration
- [ ] `infra/terraform/backend.hcl` exists with your remote state storage account details
- [ ] `infra/deployments/{env}-{region}.env` exists with your deployment configuration (environment, location, base name, resource group)
- [ ] App registration exists in Entra with a federated credential for your GitHub repo
- [ ] `gh secret list` shows `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`
- [ ] `gh variable list --env dev` shows all infrastructure variables

### What's next

Proceed to [Lab 1 — Full Azure Track](lab-1.md#full-azure-track--full-infrastructure--seeding) to deploy the Azure infrastructure and seed your databases.
