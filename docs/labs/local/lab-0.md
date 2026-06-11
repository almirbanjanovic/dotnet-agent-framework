# Lab 0 — Bootstrap (Local Track)

> **Track:** Local — Foundry only, everything else runs on your laptop.
> Looking for the Full Azure Track instead? See [`../full-azure/lab-0.md`](../full-azure/lab-0.md).

Lab 0 on the Local Track is a sanity check. There is no remote Terraform-state backend, no GitHub OIDC. [Lab 1](lab-1.md)'s `setup-local` script provisions a per-developer Foundry account, an Entra SPA app registration with localhost callbacks, and a small set of test users in your tenant.

## Prerequisites

- An **Azure subscription** where you can create a resource group + Azure AI Services account (Contributor on a subscription, or Owner on a resource group, is sufficient).
- An **Entra tenant** where you can create a SPA app registration **and** test users. In practice this means:
  - **Application Developer** (or higher) — to create the BFF app registration
  - **User Administrator** (or higher)    — to create the 8 seeded test users (Emma, James, Sarah, David, Lisa, Mike, Anna, Tom)

  Most M365 developer tenants grant both by default. Enterprise tenants typically don't — if you don't have these roles, [create a free M365 dev tenant](https://learn.microsoft.com/microsoft-365/developer/microsoft-365-developer-program) and run the Local Track there.
- Tools:
  - [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)
  - [Terraform >= 1.14.7](https://developer.hashicorp.com/terraform/install)
  - [.NET SDK 9.0+](https://dotnet.microsoft.com/download)
  - [Python 3.9+](https://www.python.org/downloads/) (required for `infra/setup-local.sh`; PowerShell path does not require Python)

## Run

```bash
az login
az account set --subscription "<your-subscription-id>"   # only if you have more than one
```

Then trust the ASP.NET Core developer certificate so the Aspire dashboard
(which serves `https://localhost:15888` and gRPC-calls itself over TLS)
loads cleanly:

```powershell
dotnet dev-certs https --trust
```

Click **Yes** on the OS trust prompt. This is a one-time, per-machine setup —
without it `dotnet run --project src/AppHost` will start, but the dashboard
will spew `AuthenticationException: ... UntrustedRoot` until you fix it.

## Verification checklist

- [ ] `az account show` returns the subscription you intend to deploy into
- [ ] `az ad signed-in-user show` returns your tenant identity (confirms the Entra side of `az login`)
- [ ] `dotnet --version` ≥ 9.0
- [ ] `terraform --version` ≥ 1.14.7
- [ ] `dotnet dev-certs https --check --trust` reports `A trusted certificate was found.`

## What's next

Proceed to [Lab 1](lab-1.md). The `setup-local` script there provisions Foundry, creates your local SPA app registration + 8 test users, and writes their UPNs and freshly-generated passwords to **`local-dev-credentials.txt`** at the repo root (gitignored, persists across runs — see the in-script comment block for the rotation rules).
