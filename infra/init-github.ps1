#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    One-time bootstrap: creates Entra app registration with OIDC for GitHub Actions
    and configures GitHub repository secrets + environment variables.

.DESCRIPTION
    This script:
    1. Creates an Entra app registration + service principal
    2. Adds an OIDC federated credential for GitHub Actions
    3. Grants Contributor on the target subscription
    4. Sets GitHub repository secrets (AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_SUBSCRIPTION_ID)
    5. Creates a GitHub environment and sets all infrastructure variables

.PARAMETER SubscriptionId
    Azure subscription ID. Defaults to the current az CLI subscription.

.PARAMETER GitHubRepo
    GitHub repository in "owner/repo" format. Defaults to the current repo via gh CLI.

.PARAMETER GitHubEnv
    GitHub Actions environment name. Defaults to "dev".

.PARAMETER AppName
    Entra app registration display name. Defaults to "github-actions-<repo-name>".

.PARAMETER SkipEntra
    Skip Entra app registration (use if already created). Requires AppClientId.

.PARAMETER AppClientId
    Existing app registration client ID (used with -SkipEntra).

.EXAMPLE
    ./init-github.ps1
    ./init-github.ps1 -SubscriptionId "12345678-..." -GitHubRepo "myorg/myrepo"
    ./init-github.ps1 -SkipEntra -AppClientId "12345678-..."
#>

param(
    [string]$SubscriptionId,
    [string]$GitHubRepo,
    [string]$GitHubEnv = "dev",
    [string]$AppName,
    [switch]$SkipEntra,
    [string]$AppClientId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Helpers ──────────────────────────────────────────────────────────────────
function Write-Step { param([string]$Message) Write-Host "`n==> $Message" -ForegroundColor Cyan }
function Write-Done { param([string]$Message) Write-Host "    ✓ $Message" -ForegroundColor Green }
function Write-Skip { param([string]$Message) Write-Host "    ⊘ $Message" -ForegroundColor Yellow }

# ── Verify prerequisites ────────────────────────────────────────────────────
Write-Step "Checking prerequisites"

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw "Azure CLI (az) is not installed. See https://learn.microsoft.com/cli/azure/install-azure-cli"
}
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI (gh) is not installed. See https://cli.github.com/"
}

# Verify az login
try { $null = az account show 2>&1; if ($LASTEXITCODE -ne 0) { throw } }
catch { throw "Not logged in to Azure CLI. Run 'az login' first." }

# Verify gh auth
try { $null = gh auth status 2>&1; if ($LASTEXITCODE -ne 0) { throw } }
catch { throw "Not logged in to GitHub CLI. Run 'gh auth login' first." }

Write-Done "az and gh authenticated"

# ── Resolve defaults ────────────────────────────────────────────────────────
if (-not $SubscriptionId) {
    $SubscriptionId = (az account show --query id -o tsv)
    Write-Done "Using current subscription: $SubscriptionId"
}

if (-not $GitHubRepo) {
    $GitHubRepo = (gh repo view --json nameWithOwner -q ".nameWithOwner" 2>$null)
    if (-not $GitHubRepo) { throw "Could not detect GitHub repo. Pass -GitHubRepo 'owner/repo' or run from inside a git repo." }
    Write-Done "Using current repo: $GitHubRepo"
}

$RepoName = ($GitHubRepo -split "/")[-1]

if (-not $AppName) {
    $AppName = "github-actions-$RepoName"
}

$TenantId = (az account show --query tenantId -o tsv)

Write-Host ""
Write-Host "╔═══════════════════════════════════════════════════════════╗"
Write-Host "║  Bootstrap Configuration                                 ║"
Write-Host "╠═══════════════════════════════════════════════════════════╣"
Write-Host "║  Subscription:   $SubscriptionId"
Write-Host "║  Tenant:         $TenantId"
Write-Host "║  GitHub repo:    $GitHubRepo"
Write-Host "║  GitHub env:     $GitHubEnv"
Write-Host "║  App name:       $AppName"
Write-Host "║  Skip Entra:     $SkipEntra"
Write-Host "╚═══════════════════════════════════════════════════════════╝"

# ── Step 1: Entra app registration ──────────────────────────────────────────
if ($SkipEntra) {
    if (-not $AppClientId) { throw "-SkipEntra requires -AppClientId" }
    Write-Skip "Skipping Entra setup (using existing app: $AppClientId)"
} else {
    Write-Step "Creating Entra app registration"

    # Check if app already exists
    $existing = az ad app list --display-name "$AppName" --query "[0].appId" -o tsv 2>$null
    if ($existing) {
        $AppClientId = $existing
        Write-Skip "App '$AppName' already exists: $AppClientId"
    } else {
        $AppClientId = az ad app create --display-name "$AppName" --query appId -o tsv
        Write-Done "Created app: $AppClientId"
    }

    # Create service principal if not exists
    $spExists = az ad sp show --id "$AppClientId" --query id -o tsv 2>$null
    if ($spExists) {
        Write-Skip "Service principal already exists"
    } else {
        $null = az ad sp create --id "$AppClientId"
        Write-Done "Created service principal"
    }

    # Add OIDC federated credential
    Write-Step "Adding OIDC federated credential"
    $credName = "github-actions-$GitHubEnv"
    $existingCred = az ad app federated-credential list --id "$AppClientId" --query "[?name=='$credName'].name" -o tsv 2>$null
    if ($existingCred) {
        Write-Skip "Federated credential '$credName' already exists"
    } else {
        $credParams = @{
            name = $credName
            issuer = "https://token.actions.githubusercontent.com"
            subject = "repo:${GitHubRepo}:environment:${GitHubEnv}"
            audiences = @("api://AzureADTokenExchange")
            description = "GitHub Actions OIDC for $RepoName ($GitHubEnv)"
        } | ConvertTo-Json -Compress

        $null = az ad app federated-credential create --id "$AppClientId" --parameters $credParams
        Write-Done "Added federated credential for repo:${GitHubRepo}:environment:${GitHubEnv}"
    }

    # Grant Contributor
    Write-Step "Granting Contributor role on subscription"
    $roleExists = az role assignment list --assignee "$AppClientId" --role "Contributor" --scope "/subscriptions/$SubscriptionId" --query "[0].id" -o tsv 2>$null
    if ($roleExists) {
        Write-Skip "Contributor role already assigned"
    } else {
        $null = az role assignment create --assignee "$AppClientId" --role "Contributor" --scope "/subscriptions/$SubscriptionId"
        Write-Done "Granted Contributor on subscription $SubscriptionId"
    }
}

# ── Step 2: GitHub repository secrets ────────────────────────────────────────
Write-Step "Setting GitHub repository secrets"

gh secret set AZURE_CLIENT_ID --repo "$GitHubRepo" --body "$AppClientId"
Write-Done "AZURE_CLIENT_ID"

gh secret set AZURE_TENANT_ID --repo "$GitHubRepo" --body "$TenantId"
Write-Done "AZURE_TENANT_ID"

gh secret set AZURE_SUBSCRIPTION_ID --repo "$GitHubRepo" --body "$SubscriptionId"
Write-Done "AZURE_SUBSCRIPTION_ID"

# ── Step 3: GitHub environment variables ─────────────────────────────────────
Write-Step "Setting GitHub environment variables ($GitHubEnv)"

$envVars = [ordered]@{
    # Backend / bootstrap
    RESOURCE_GROUP                        = "rg-agentic-ai-centralus"
    LOCATION                              = "centralus"
    STORAGE_ACCOUNT                       = "stagenticaicentralus"
    STORAGE_ACCOUNT_SKU                   = "Standard_LRS"
    STORAGE_ACCOUNT_ENCRYPTION_SERVICES   = "blob"
    STORAGE_ACCOUNT_MIN_TLS_VERSION       = "TLS1_2"
    STORAGE_ACCOUNT_PUBLIC_NETWORK_ACCESS = "Enabled"
    TERRAFORM_STATE_CONTAINER             = "tfstate"
    TERRAFORM_STATE_BLOB                  = "agentic-ai.tfstate"
    TERRAFORM_WORKING_DIRECTORY           = "infra/terraform"

    # Infrastructure (mapped to TF_VAR_ in workflows)
    TAGS                            = "{}"
    ENVIRONMENT                     = "dev"
    BASE_NAME                       = "agentic-ai"
    COGNITIVE_ACCOUNT_KIND          = "AIServices"
    OAI_SKU_NAME                    = "S0"
    OAI_DEPLOYMENT_SKU_NAME         = "GlobalStandard"
    OAI_DEPLOYMENT_MODEL_FORMAT     = "OpenAI"
    OAI_DEPLOYMENT_MODEL_NAME       = "gpt-4.1"
    OAI_DEPLOYMENT_MODEL_VERSION    = "2025-04-14"
    OAI_VERSION_UPGRADE_OPTION      = "NoAutoUpgrade"
    CREATE_EMBEDDING_DEPLOYMENT     = "true"
    EMBEDDING_MODEL_NAME            = "text-embedding-ada-002"
    EMBEDDING_MODEL_VERSION         = "2"
    EMBEDDING_SKU_NAME              = "Standard"
    EMBEDDING_CAPACITY              = "10"
    COSMOS_PROJECT_NAME             = "dotnetagent"
    COSMOS_AGENTS_DATABASE_NAME      = "agents"
    COSMOS_AGENT_STATE_CONTAINER_NAME = "workshop_agent_state_store"
    SQL_DATABASE_NAME               = "contoso-outdoors"
    SQL_ADMIN_LOGIN                 = "sqladmin"
    STORAGE_PROJECT_NAME            = "dotnetagent"
    SEARCH_SKU                      = "basic"
    SEARCH_INDEX_NAME               = "knowledge-documents"
    ACR_PROJECT_NAME                = "dotnetagent"
    CREATE_ACR                      = "true"
    ACR_SKU                         = "Premium"
    EXISTING_ACR_NAME               = ""
    AKS_KUBERNETES_VERSION          = ""
    AKS_NODE_VM_SIZE                = "Standard_D4s_v5"
    AKS_NODE_COUNT                  = "2"
    AKS_AUTO_SCALING_ENABLED        = "true"
    AKS_NODE_MIN_COUNT              = "1"
    AKS_NODE_MAX_COUNT              = "5"
    AKS_OS_DISK_SIZE_GB             = "64"
    AKS_LOG_RETENTION_DAYS          = "30"
}

foreach ($kv in $envVars.GetEnumerator()) {
    gh variable set $kv.Key --repo "$GitHubRepo" --env "$GitHubEnv" --body "$($kv.Value)"
}

Write-Done "Set $($envVars.Count) environment variables"

# ── Summary ──────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "╔═══════════════════════════════════════════════════════════╗"
Write-Host "║  Bootstrap Complete                                      ║"
Write-Host "╠═══════════════════════════════════════════════════════════╣"
Write-Host "║  App registration:  $AppClientId"
Write-Host "║  Tenant ID:         $TenantId"
Write-Host "║  Subscription ID:   $SubscriptionId"
Write-Host "║  GitHub repo:       $GitHubRepo"
Write-Host "║  GitHub env:        $GitHubEnv"
Write-Host "║  Repo secrets:      3 (AZURE_CLIENT_ID, TENANT_ID, SUBSCRIPTION_ID)"
Write-Host "║  Env variables:     $($envVars.Count)"
Write-Host "╚═══════════════════════════════════════════════════════════╝"
Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Run infra/init-backend.ps1 to bootstrap Terraform state"
Write-Host "  2. Proceed to Lab 1"
