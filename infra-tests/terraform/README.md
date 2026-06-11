# Terraform Tests for local-dev

This directory contains tests for the `infra/terraform/local-dev/` Terraform module.

## Overview

The local-dev module provisions Azure resources for local development:
- **Azure Resource Group** — container for all resources
- **Azure AI Services Account** — provides API endpoint for models
- **Chat Model Deployment** — GPT-4 model for conversational AI
- **Embedding Model Deployment** — Text embedding model for semantic search

## Tests

All 4 tests validate core configuration without requiring Azure credentials or actual resource creation.

| # | Test | What it verifies |
|---|------|------------------|
| 1 | `terraform init -backend=false` passes | Module/provider wiring initializes cleanly |
| 2 | `terraform validate` passes | HCL syntax and references are valid |
| 3 | Outputs are defined | `foundry_project_endpoint`, `chat_deployment_name`, `embedding_deployment_name`, `tenant_id`, `bff_client_id`, `customer_map_json` exist |
| 4 | Variables have sensible defaults | All expected local-dev variables include defaults |

## Running Tests

### Option 1: PowerShell (Windows)
```powershell
.\infra-tests\terraform\run-tests.ps1
```

### Option 2: Bash (Unix/Linux/Git Bash)
```bash
bash ./infra-tests/terraform/run-tests.sh
```

## Test Details

Each test is designed to fail fast if there's a configuration issue:

1. **terraform init** — Validates that the module can be initialized without a backend
2. **terraform validate** — Checks HCL syntax and semantic validity
3. **Outputs check** — Ensures all required outputs are declared in `outputs.tf`
4. **Variables check** — Verifies all variables are properly defined with sensible defaults

## Why No `terraform test` Framework?

The native Terraform test framework (`terraform test`) requires:
- Active Azure provider credentials
- Network access to Azure
- Billable resource creation

Since these aren't available in CI/CD pipelines without special configuration, we use shell-based validation instead. This approach:
- ✅ Runs without credentials
- ✅ Executes in any CI/CD environment
- ✅ Validates syntax and configuration
- ✅ Completes in <1 second
- ✅ Deterministic (no external dependencies)

## Exit Codes

- **0** — All tests passed
- **1** — One or more tests failed

## Variables with Defaults

| Variable | Default | Purpose |
|----------|---------|---------|
| `location` | `centralus` | Azure region for resources |
| `base_name` | `dotnetagent` | Prefix for resource names |
| `environment` | `localdev` | Environment tag (used in naming) |
| `chat_model_name` | `gpt-4.1` | Chat model to deploy |
| `chat_model_version` | `2025-04-14` | Chat model version |
| `embedding_model_name` | `text-embedding-3-small` | Embedding model to deploy |
| `embedding_model_version` | `1` | Embedding model version |
| `resource_group_name` | `null` (generates name) | Optional override for RG name |

All variables have sensible defaults and can be used without customization for local development.

## Outputs

| Output | Type | Sensitive | Usage |
|--------|------|-----------|-------|
| `foundry_project_endpoint` | string | ❌ | Foundry project endpoint URL |
| `chat_deployment_name` | string | ❌ | Chat model deployment name |
| `embedding_deployment_name` | string | ❌ | Embedding model deployment name |
| `tenant_id` | string | ❌ | Azure tenant ID for `DefaultAzureCredential` |
| `bff_client_id` | string | ❌ | SPA app registration client ID |
| `test_user_upns` | map | ❌ | Map of key (`emma`, ...) → UPN |
| `test_user_passwords` | map | ✅ | Map of key → generated password |
| `customer_map_json` | string | ❌ | JSON: UPN → customer ID for `AzureAd:CustomerMap` |

Auth uses `DefaultAzureCredential` (no API key output) — enforced by [`NoApiKeyTests`](../../src-tests/Contoso.SimpleAgent.Tests/NoApiKeyTests.cs).
