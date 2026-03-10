# Labs

This folder contains all .NET agent labs and tools. Each subfolder is a standalone project.

> **Prerequisite:** Infrastructure must be deployed before running any lab. See **[../infra/README.md](../infra/README.md)**.

## Configure app settings

After infrastructure is deployed, run the **config-sync** tool to pull secrets from Key Vault into `src/appsettings.json`:

```bash
cd src/config-sync
dotnet run -- <your-keyvault-uri>
```

You can find the Key Vault URI with:

```bash
terraform output keyvault_uri
```

This populates `src/appsettings.json` (gitignored) with all the values your apps need:

| Key                                  | Description                          | Populated from Key Vault secret        |
|--------------------------------------|--------------------------------------|----------------------------------------|
| `AZURE_OPENAI_ENDPOINT`              | Azure OpenAI endpoint                | `AZURE-OPENAI-ENDPOINT`               |
| `AZURE_OPENAI_DEPLOYMENT_NAME`       | Chat model deployment name           | `AZURE-OPENAI-DEPLOYMENT-NAME`        |
| `AZURE_OPENAI_API_KEY`               | API key for authentication           | `AZURE-OPENAI-API-KEY`                |
| `AZURE_OPENAI_EMBEDDING_DEPLOYMENT`  | Embedding model name                 | `AZURE-OPENAI-EMBEDDING-DEPLOYMENT`   |
| `COSMOSDB_OPERATIONAL_ENDPOINT`      | Operational Cosmos DB endpoint       | `COSMOSDB-OPERATIONAL-ENDPOINT`       |
| `COSMOSDB_OPERATIONAL_KEY`           | Operational Cosmos DB key            | `COSMOSDB-OPERATIONAL-KEY`            |
| `COSMOSDB_OPERATIONAL_DATABASE`      | Operational database name            | `COSMOSDB-OPERATIONAL-DATABASE`       |
| `COSMOSDB_KNOWLEDGE_ENDPOINT`        | Knowledge (RAG) Cosmos DB endpoint   | `COSMOSDB-KNOWLEDGE-ENDPOINT`         |
| `COSMOSDB_KNOWLEDGE_KEY`             | Knowledge Cosmos DB key              | `COSMOSDB-KNOWLEDGE-KEY`              |
| `COSMOSDB_KNOWLEDGE_DATABASE`        | Knowledge database name              | `COSMOSDB-KNOWLEDGE-DATABASE`         |
| `COSMOSDB_AGENTS_ENDPOINT`           | Agents Cosmos DB endpoint            | `COSMOSDB-AGENTS-ENDPOINT`            |
| `COSMOSDB_AGENTS_KEY`                | Agents Cosmos DB key                 | `COSMOSDB-AGENTS-KEY`                 |
| `COSMOSDB_AGENTS_DATABASE`           | Agents database name                 | `COSMOSDB-AGENTS-DATABASE`            |
| `STORAGE_IMAGES_ENDPOINT`            | Product images blob endpoint         | `STORAGE-IMAGES-ENDPOINT`             |
| `STORAGE_IMAGES_ACCOUNT_NAME`        | Product images storage account name  | `STORAGE-IMAGES-ACCOUNT-NAME`         |
| `STORAGE_IMAGES_CONTAINER`           | Product images container name        | `STORAGE-IMAGES-CONTAINER`            |
| `STORAGE_IMAGES_KEY`                 | Product images storage key           | `STORAGE-IMAGES-KEY`                  |

The config-sync tool uses `DefaultAzureCredential` — make sure you're logged in with `az login`.

> **In AKS:** Apps read the same keys from environment variables injected by Helm, so no Key Vault dependency at runtime.

The `appsettings.json` is shared across all labs — each project references it via a relative path from `src/`.

## Labs

| # | File | Description |
|---|------|-------------|
| 0 | [Lab 0 — Bootstrap](../docs/lab-0.md) | One-time setup: Entra app registration (CI/CD), Terraform config files, state backend |
| 1 | [Lab 1 — Infrastructure, Validation & Data Seeding](../docs/lab-1.md) | Deploy Azure infrastructure, validate with simple-agent, seed Cosmos DB with CRM and RAG data |

## Running a project

From the project directory (e.g., `src/simple-agent/`):

```bash
dotnet restore
dotnet run
```
