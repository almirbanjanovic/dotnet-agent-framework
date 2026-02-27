# dotnet-agent-framework

Hands-on .NET Agent Framework tutorials based on Microsoft Learn:
https://learn.microsoft.com/en-us/agent-framework/overview/?pivots=programming-language-csharp

## What this repo contains

- `src/getting-started/01-first-agent` → first runnable C# agent sample
- `src/getting-started/02-add-tools` → tool-enabled agents
- `src/getting-started/03-multi-turn-conversations` → conversation state patterns
- `src/getting-started/04-memory-and-persistence` → memory/persistence patterns
- `src/getting-started/05-first-workflow` → workflow fundamentals
- `src/getting-started/06-hosting-your-agent` → hosting/deployment-oriented sample
- `infra/getting-started/bicep` → Bicep IaC baseline
- `infra/getting-started/terraform` → Terraform IaC baseline

## Prerequisites

- .NET SDK 9.0+
- Azure CLI (`az`) authenticated to your target subscription
- Terraform 1.14.6+

## Run the first sample

1. Go to the first sample:

	```bash
	cd src/getting-started/01-first-agent
	```

2. Provide config via one of:
	- `appsettings.Development.json` (local file)
	- environment variables

	Required keys:
	- `AZURE_OPENAI_ENDPOINT`
	- `AZURE_OPENAI_DEPLOYMENT_NAME`

3. Run:

	```bash
	dotnet restore
	dotnet run
	```

## Terraform quick workflow (local)

From `infra/getting-started/terraform/`:

```bash
terraform fmt -recursive
terraform init -backend=false
terraform validate
terraform plan -refresh=false -var-file="terraform.tfvars"
```

Use this for fast local validation without remote state.

## Terraform remote state bootstrap (for local + CI/CD parity)

### 1) Bootstrap backend resources (one-time)

Create backend resources in Azure (resource group, storage account, container) using your backend bootstrap script.

### 2) Create local backend config

Create a local-only `backend.hcl` in `infra/getting-started/terraform/` (do not commit):

```hcl
resource_group_name  = "<tfstate-rg-name>"
storage_account_name = "<tfstate-storage-account-name>"
container_name       = "tfstate"
key                  = "getting-started/dev.tfstate"
```

### 3) Run real plan/apply/destroy with remote state

```bash
terraform init -reconfigure -backend-config=backend.hcl
terraform validate
terraform plan -var-file="terraform.tfvars"
terraform apply -var-file="terraform.tfvars"
```

Destroy:

```bash
terraform destroy -var-file="terraform.tfvars"
```

If you need to migrate existing local state:

```bash
terraform init -migrate-state -backend-config=backend.hcl
```

## Notes

- Provider versions are pinned in `infra/getting-started/terraform/providers.tf` with `~>` constraints.
- Local tfvars are intentionally ignored under `infra/.gitignore`.
