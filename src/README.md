# Labs

This folder contains all .NET agent labs and tools. Each subfolder is a standalone project.

> **Prerequisite:** Infrastructure must be deployed before running any lab. See [Lab 0](../docs/lab-0.md) and [Lab 1](../docs/lab-1.md).

## Configure app settings

After infrastructure is deployed, run the **config-sync** tool to pull 17 secrets from Key Vault into `src/appsettings.json`. See [Lab 1 Step 2](../docs/lab-1.md#step-2--configure-app-settings) for full instructions.

> **In AKS:** Apps read the same keys from environment variables injected by Helm, so no Key Vault dependency at runtime.

The `appsettings.json` is shared across all labs — each project references it via a relative path from `src/`.

## Labs

| # | File | Description |
|---|------|-------------|
| 0 | [Lab 0 — Bootstrap](../docs/lab-0.md) | One-time setup: Terraform config files, remote state backend, CI/CD configuration |
| 1 | [Lab 1 — Infrastructure, Validation & Data Seeding](../docs/lab-1.md) | Deploy Azure infrastructure, validate with simple-agent, seed Cosmos DB with CRM and RAG data |

## Running a project

From the project directory (e.g., `src/simple-agent/`):

```bash
dotnet restore
dotnet run
```
