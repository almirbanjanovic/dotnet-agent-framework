# Labs

This folder contains all .NET agent labs. Each subfolder is a standalone lab project.

> **Prerequisite:** Infrastructure must be deployed before running any lab. See **[../infra/README.md](../infra/README.md)**.

## Configure app settings

After infrastructure is deployed, create `src/appsettings.json` (this file is gitignored):

```json
{
  "AZURE_OPENAI_ENDPOINT": "<your-endpoint>",
  "AZURE_OPENAI_DEPLOYMENT_NAME": "<your-deployment-name>",
  "AZURE_OPENAI_API_KEY": "<your-api-key>"
}
```

These values are shown in Terraform outputs after `terraform apply`, or you can find them in the [Azure AI Foundry portal](https://ai.azure.com) under **Models + endpoints**.

| Key                            | Description                | Source                                    |
|--------------------------------|----------------------------|-------------------------------------------|
| `AZURE_OPENAI_ENDPOINT`        | Azure OpenAI endpoint      | `terraform output openai_endpoint`        |
| `AZURE_OPENAI_DEPLOYMENT_NAME` | Model deployment name      | `terraform output openai_deployment_name` |
| `AZURE_OPENAI_API_KEY`         | API key for authentication | `terraform output openai_api_key`         |

> **Note:** Use the `.openai.azure.com` endpoint (shown in AI Foundry), not the `.cognitiveservices.azure.com` endpoint from the Azure Portal.

The `appsettings.json` is shared across all labs — each project references it via a relative path from `src/`.

## Labs

| Lab | Folder | Description |
|-----|--------|-------------|
| 1   | `simple-agent/` | Validate infrastructure — simple console app that calls Azure OpenAI to confirm your deployment and app settings are configured correctly |

## Running a lab

From the lab directory (e.g., `src/simple-agent/`):

```bash
dotnet restore
dotnet run
```
