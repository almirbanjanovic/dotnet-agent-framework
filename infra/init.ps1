#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    One-time bootstrap for the .NET Agent Framework workshop.

.DESCRIPTION
    Performs all Lab 0 setup in a single execution:
    1. Authenticates to Azure (pick subscription) and GitHub (detect/create repo)
    2. Creates Entra app registration with OIDC for GitHub Actions + Contributor RBAC
    3. Creates GitHub environment, secrets, and variables
    4. Creates Azure resource group, storage account, blob container (Terraform state)
    5. Generates terraform.tfvars and backend.hcl

.PARAMETER SubscriptionId
    Azure subscription ID. If omitted, you'll be prompted to select one.

.PARAMETER GitHubEnv
    GitHub Actions environment name. Defaults to "dev".

.PARAMETER Location
    Azure region for all resources. Defaults to "eastus2".

.PARAMETER BaseName
    Project base name used in resource naming. Defaults to "dotnetagent".

.PARAMETER SkipEntra
    Skip Entra app registration. Requires AppClientId.

.PARAMETER AppClientId
    Existing app registration client ID (used with -SkipEntra).

.EXAMPLE
    ./init.ps1
    ./init.ps1 -Location "centralus" -BaseName "myproject"
    ./init.ps1 -SkipEntra -AppClientId "12345678-..."
#>

param(
    [string]$SubscriptionId,
    [string]$GitHubEnv = "dev",
    [string]$Location = "eastus2",
    [string]$BaseName = "dotnetagent",
    [switch]$SkipEntra,
    [string]$AppClientId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ScriptDir    = $PSScriptRoot
$TerraformDir = "$ScriptDir\terraform"

# ── Helpers ──────────────────────────────────────────────────────────────────
function Write-Banner {
    Write-Host ""
    Write-Host "  ╔═══════════════════════════════════════════════════════╗" -ForegroundColor DarkCyan
    Write-Host "  ║                                                       ║" -ForegroundColor DarkCyan
    Write-Host "  ║   .NET Agent Framework — Lab 0 Bootstrap              ║" -ForegroundColor DarkCyan
    Write-Host "  ║                                                       ║" -ForegroundColor DarkCyan
    Write-Host "  ║   This script sets up everything you need:            ║" -ForegroundColor DarkCyan
    Write-Host "  ║     1. Authenticate (Azure + GitHub)                  ║" -ForegroundColor DarkCyan
    Write-Host "  ║     2. Entra app + OIDC + RBAC                        ║" -ForegroundColor DarkCyan
    Write-Host "  ║     3. GitHub environment, secrets, variables         ║" -ForegroundColor DarkCyan
    Write-Host "  ║     4. Azure backend (RG, storage, container)         ║" -ForegroundColor DarkCyan
    Write-Host "  ║     5. Config files (terraform.tfvars, backend.hcl)   ║" -ForegroundColor DarkCyan
    Write-Host "  ║                                                       ║" -ForegroundColor DarkCyan
    Write-Host "  ╚═══════════════════════════════════════════════════════╝" -ForegroundColor DarkCyan
    Write-Host ""
}

function Write-Phase {
    param([int]$Number, [string]$Title)
    Write-Host ""
    Write-Host "  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
    Write-Host "  Phase $Number — $Title" -ForegroundColor Cyan
    Write-Host "  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
}

function Write-Step  { param([string]$Message) Write-Host "  → $Message" -ForegroundColor White }
function Write-Done  { param([string]$Message) Write-Host "    ✓ $Message" -ForegroundColor Green }
function Write-Skip  { param([string]$Message) Write-Host "    · $Message" -ForegroundColor DarkGray }

function Write-PhaseSummary {
    param([int]$Number, [hashtable]$Items)
    Write-Host ""
    Write-Host "    ┌ Phase $Number complete ─────────────────────────────────┐" -ForegroundColor Green
    foreach ($kv in $Items.GetEnumerator()) {
        Write-Host "    │  $($kv.Key): " -ForegroundColor Green -NoNewLine
        Write-Host "$($kv.Value)"
    }
    Write-Host "    └─────────────────────────────────────────────────────┘" -ForegroundColor Green
    $response = Read-Host "    Continue to next phase? (Y/n)"
    if ($response -eq 'n' -or $response -eq 'N') {
        Write-Host "    Stopped by user." -ForegroundColor Yellow
        exit 0
    }
}

# ── Derived names ────────────────────────────────────────────────────────────
$ResourceGroup  = "rg-$BaseName-$GitHubEnv-$Location"
$StorageAccount = ("st" + ($ResourceGroup -replace '^rg-', '' -replace '[^a-z0-9]', '').ToLower())
if ($StorageAccount.Length -gt 24) { $StorageAccount = $StorageAccount.Substring(0, 24) }

# ═══════════════════════════════════════════════════════════════════════════════
# Prerequisites
# ═══════════════════════════════════════════════════════════════════════════════

Write-Banner

Write-Step "Checking & installing prerequisites"

$prerequisites = @{
    "az"        = @{ Name = "Azure CLI";   WinGet = "Microsoft.AzureCLI" }
    "gh"        = @{ Name = "GitHub CLI";  WinGet = "GitHub.cli" }
    "terraform" = @{ Name = "Terraform";   WinGet = "Hashicorp.Terraform" }
    "dotnet"    = @{ Name = ".NET SDK";    WinGet = "Microsoft.DotNet.SDK.9" }
}

foreach ($cmd in $prerequisites.Keys) {
    if (Get-Command $cmd -ErrorAction SilentlyContinue) {
        Write-Done "$($prerequisites[$cmd].Name) ($cmd)"
    } else {
        $pkg = $prerequisites[$cmd]
        Write-Host "    Installing $($pkg.Name)..." -ForegroundColor Yellow
        if (Get-Command winget -ErrorAction SilentlyContinue) {
            winget install --id $pkg.WinGet --accept-source-agreements --accept-package-agreements | Out-Null
            $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")
            if (Get-Command $cmd -ErrorAction SilentlyContinue) {
                Write-Done "$($pkg.Name) installed"
            } else {
                throw "Failed to install $($pkg.Name). Install manually and re-run."
            }
        } else {
            throw "$($pkg.Name) is not installed and winget is not available. Install manually: see docs/lab-0.md"
        }
    }
}

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 1 — Authenticate
# ═══════════════════════════════════════════════════════════════════════════════

Write-Phase -Number 1 -Title "Authenticate"

# ── Azure ────────────────────────────────────────────────────────────────────
Write-Step "Signing in to Azure"
az login | Out-Null

if (-not $SubscriptionId) {
    Write-Host ""
    az account list --query "[].{Name:name, Id:id, IsDefault:isDefault}" -o table
    Write-Host ""
    $currentSub = az account show --query name -o tsv
    $currentId  = az account show --query id -o tsv
    Write-Host "    Current subscription: " -NoNewLine
    Write-Host "$currentSub ($currentId)" -ForegroundColor Cyan
    $changeIt = Read-Host "    Use this subscription? (Y/n)"
    if ($changeIt -eq 'n' -or $changeIt -eq 'N') {
        $SubscriptionId = Read-Host "    Enter subscription ID"
        az account set --subscription $SubscriptionId
    } else {
        $SubscriptionId = $currentId
    }
} else {
    az account set --subscription $SubscriptionId
}

$SubName  = az account show --query name -o tsv
$TenantId = az account show --query tenantId -o tsv
Write-Done "Azure: $SubName ($SubscriptionId)"

# ── GitHub ───────────────────────────────────────────────────────────────────
Write-Step "Signing in to GitHub"
gh auth login

$GitHubRepo = gh repo view --json nameWithOwner -q ".nameWithOwner" 2>$null

if ($GitHubRepo) {
    Write-Host "    Detected repo: " -NoNewLine
    Write-Host "$GitHubRepo" -ForegroundColor Cyan
    $confirmRepo = Read-Host "    Use this repo? (Y/n)"
    if ($confirmRepo -eq 'n' -or $confirmRepo -eq 'N') {
        $GitHubRepo = Read-Host "    Enter repo (owner/name)"
    }
} else {
    Write-Host "    No GitHub repo detected." -ForegroundColor Yellow
    $ghUser = gh api user --jq .login 2>$null
    $defaultName = Split-Path -Leaf (git rev-parse --show-toplevel 2>$null)
    if (-not $defaultName) { $defaultName = "dotnet-agent-framework" }

    $action = Read-Host "    (C)reate new repo '$ghUser/$defaultName' or (E)nter existing? [C/e]"
    if ($action -eq 'e' -or $action -eq 'E') {
        $GitHubRepo = Read-Host "    Enter repo (owner/name)"
    } else {
        Write-Host "    Creating repo $ghUser/$defaultName..."
        gh repo create "$defaultName" --private --source . --push
        $GitHubRepo = "$ghUser/$defaultName"
        Write-Done "Created $GitHubRepo"
    }
}

if (-not $GitHubRepo) { throw "No GitHub repository configured." }
$RepoName = ($GitHubRepo -split "/")[-1]
Write-Done "GitHub: $GitHubRepo"

# ── Show configuration & confirm ─────────────────────────────────────────────
Write-PhaseSummary -Number 1 -Items ([ordered]@{
    "Subscription"   = "$SubName ($SubscriptionId)"
    "Tenant"         = $TenantId
    "GitHub repo"    = $GitHubRepo
    "Location"       = $Location
    "Base name"      = $BaseName
    "Environment"    = $GitHubEnv
    "Resource group" = $ResourceGroup
})

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 2 — Entra app registration + OIDC + RBAC
# ═══════════════════════════════════════════════════════════════════════════════

if ($SkipEntra) {
    if (-not $AppClientId) { throw "-SkipEntra requires -AppClientId" }
    Write-Skip "Skipping Entra setup (using existing app: $AppClientId)"
} else {
    Write-Phase -Number 2 -Title "Entra app registration + OIDC + RBAC"

    $AppName = "github-actions-$RepoName"

    Write-Step "Creating app registration"
    $existing = az ad app list --display-name "$AppName" --query "[0].appId" -o tsv 2>$null
    if ($existing) {
        $AppClientId = $existing
        Write-Skip "App '$AppName' already exists: $AppClientId"
    } else {
        $AppClientId = az ad app create --display-name "$AppName" --query appId -o tsv
        Write-Done "Created app: $AppClientId"
    }

    $spExists = az ad sp show --id "$AppClientId" --query id -o tsv 2>$null
    if ($spExists) {
        Write-Skip "Service principal already exists"
    } else {
        $null = az ad sp create --id "$AppClientId"
        Write-Done "Created service principal"
    }

    Write-Step "Adding OIDC federated credential"
    $credName = "$RepoName-$GitHubEnv"
    $existingCred = az ad app federated-credential list --id "$AppClientId" --query "[?name=='$credName'].name" -o tsv 2>$null
    if ($existingCred) {
        Write-Skip "Federated credential '$credName' already exists"
    } else {
        $credParams = @{
            name        = $credName
            issuer      = "https://token.actions.githubusercontent.com"
            subject     = "repo:${GitHubRepo}:environment:${GitHubEnv}"
            audiences   = @("api://AzureADTokenExchange")
            description = "GitHub Actions OIDC for $RepoName ($GitHubEnv)"
        } | ConvertTo-Json -Compress
        $null = az ad app federated-credential create --id "$AppClientId" --parameters $credParams
        Write-Done "Federated credential for repo:${GitHubRepo}:environment:${GitHubEnv}"
    }

    Write-Step "Granting Contributor role on subscription"
    $roleExists = az role assignment list --assignee "$AppClientId" --role "Contributor" --scope "/subscriptions/$SubscriptionId" --query "[0].id" -o tsv 2>$null
    if ($roleExists) {
        Write-Skip "Contributor role already assigned"
    } else {
        $null = az role assignment create --assignee "$AppClientId" --role "Contributor" --scope "/subscriptions/$SubscriptionId"
        Write-Done "Contributor granted on $SubscriptionId"
    }

    Write-PhaseSummary -Number 2 -Items ([ordered]@{
        "App registration" = "$AppName ($AppClientId)"
        "OIDC subject"     = "repo:${GitHubRepo}:environment:${GitHubEnv}"
        "Credential name"  = $credName
        "RBAC"             = "Contributor on subscription"
    })
}

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 3 — GitHub environment, secrets, variables
# ═══════════════════════════════════════════════════════════════════════════════

Write-Phase -Number 3 -Title "GitHub environment, secrets, variables"

Write-Step "Setting repository secrets"
gh secret set AZURE_CLIENT_ID --repo "$GitHubRepo" --body "$AppClientId"
Write-Done "AZURE_CLIENT_ID"
gh secret set AZURE_TENANT_ID --repo "$GitHubRepo" --body "$TenantId"
Write-Done "AZURE_TENANT_ID"
gh secret set AZURE_SUBSCRIPTION_ID --repo "$GitHubRepo" --body "$SubscriptionId"
Write-Done "AZURE_SUBSCRIPTION_ID"

Write-Step "Setting environment variables ($GitHubEnv)"

$envVars = [ordered]@{
    RESOURCE_GROUP                        = $ResourceGroup
    LOCATION                              = $Location
    STORAGE_ACCOUNT                       = $StorageAccount
    STORAGE_ACCOUNT_SKU                   = "Standard_LRS"
    STORAGE_ACCOUNT_ENCRYPTION_SERVICES   = "blob"
    STORAGE_ACCOUNT_MIN_TLS_VERSION       = "TLS1_2"
    STORAGE_ACCOUNT_PUBLIC_NETWORK_ACCESS = "Enabled"
    TERRAFORM_STATE_CONTAINER             = "tfstate"
    TERRAFORM_STATE_BLOB                  = "$GitHubEnv.tfstate"
    TERRAFORM_WORKING_DIRECTORY           = "infra/terraform"
    TAGS                              = "{}"
    ENVIRONMENT                       = $GitHubEnv
    BASE_NAME                         = $BaseName
    COGNITIVE_ACCOUNT_KIND            = "AIServices"
    OAI_SKU_NAME                      = "S0"
    OAI_DEPLOYMENT_SKU_NAME           = "GlobalStandard"
    OAI_DEPLOYMENT_MODEL_FORMAT       = "OpenAI"
    OAI_DEPLOYMENT_MODEL_NAME         = "gpt-4.1"
    OAI_DEPLOYMENT_MODEL_VERSION      = "2025-04-14"
    OAI_VERSION_UPGRADE_OPTION        = "NoAutoUpgrade"
    CREATE_EMBEDDING_DEPLOYMENT       = "true"
    EMBEDDING_MODEL_NAME              = "text-embedding-ada-002"
    EMBEDDING_MODEL_VERSION           = "2"
    EMBEDDING_SKU_NAME                = "Standard"
    EMBEDDING_CAPACITY                = "10"
    COSMOS_PROJECT_NAME               = $BaseName
    COSMOS_AGENTS_DATABASE_NAME       = "agents"
    COSMOS_AGENT_STATE_CONTAINER_NAME = "workshop_agent_state_store"
    SQL_DATABASE_NAME                 = "contoso-outdoors"
    SQL_ADMIN_LOGIN                   = "sqladmin"
    STORAGE_PROJECT_NAME              = $BaseName
    SEARCH_SKU                        = "basic"
    SEARCH_INDEX_NAME                 = "knowledge-documents"
    ACR_PROJECT_NAME                  = $BaseName
    CREATE_ACR                        = "true"
    ACR_SKU                           = "Premium"
    EXISTING_ACR_NAME                 = ""
    AKS_KUBERNETES_VERSION            = ""
    AKS_NODE_VM_SIZE                  = "Standard_D4s_v5"
    AKS_NODE_COUNT                    = "2"
    AKS_AUTO_SCALING_ENABLED          = "true"
    AKS_NODE_MIN_COUNT                = "1"
    AKS_NODE_MAX_COUNT                = "5"
    AKS_OS_DISK_SIZE_GB               = "64"
    AKS_LOG_RETENTION_DAYS            = "30"
}

foreach ($kv in $envVars.GetEnumerator()) {
    gh variable set $kv.Key --repo "$GitHubRepo" --env "$GitHubEnv" --body "$($kv.Value)"
}
Write-Done "Set $($envVars.Count) environment variables in '$GitHubEnv'"

Write-PhaseSummary -Number 3 -Items ([ordered]@{
    "Secrets"       = "AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_SUBSCRIPTION_ID"
    "Environment"   = $GitHubEnv
    "Env variables" = "$($envVars.Count)"
})

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 4 — Azure backend resources
# ═══════════════════════════════════════════════════════════════════════════════

Write-Phase -Number 4 -Title "Azure backend resources"

Write-Step "Creating resource group"
$null = az group show --name $ResourceGroup 2>&1
if ($LASTEXITCODE -ne 0) {
    az group create --name $ResourceGroup --location $Location | Out-Null
    Write-Done "Created $ResourceGroup in $Location"
} else {
    Write-Skip "$ResourceGroup already exists"
}

Write-Step "Creating storage account for Terraform state"
$WaitTime = 30
$null = az storage account show --resource-group $ResourceGroup --name $StorageAccount 2>&1
if ($LASTEXITCODE -ne 0) {
    az storage account create `
        --resource-group $ResourceGroup --name $StorageAccount `
        --sku "Standard_LRS" --encryption-services blob `
        --min-tls-version "TLS1_2" --location $Location `
        --public-network-access Enabled | Out-Null
    Write-Host "    Waiting ${WaitTime}s for storage account..."
    Start-Sleep -Seconds $WaitTime
    Write-Done "Created $StorageAccount"
} else {
    Write-Skip "$StorageAccount already exists"
    az storage account update --name $StorageAccount --resource-group $ResourceGroup --public-network-access Enabled | Out-Null
    Start-Sleep -Seconds $WaitTime
}

$ContainerName = "tfstate"
$null = az storage container show --name $ContainerName --account-name $StorageAccount --auth-mode login 2>&1
if ($LASTEXITCODE -ne 0) {
    az storage container create --name $ContainerName --account-name $StorageAccount --auth-mode login | Out-Null
    Write-Done "Created container $ContainerName"
} else {
    Write-Skip "Container $ContainerName already exists"
}

Write-Step "Locking down state storage"
az storage account update --name $StorageAccount --resource-group $ResourceGroup --public-network-access Disabled | Out-Null
Write-Done "Public access disabled"

Write-PhaseSummary -Number 4 -Items ([ordered]@{
    "Resource group"  = $ResourceGroup
    "Storage account" = $StorageAccount
    "Container"       = $ContainerName
    "Public access"   = "Disabled"
})

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 5 — Generate config files
# ═══════════════════════════════════════════════════════════════════════════════

Write-Phase -Number 5 -Title "Generate configuration files"

Set-Content -Path "$TerraformDir\backend.hcl" -Encoding UTF8 -Value @"
resource_group_name  = "$ResourceGroup"
storage_account_name = "$StorageAccount"
container_name       = "$ContainerName"
key                  = "$GitHubEnv.tfstate"
"@
Write-Done "backend.hcl"

Set-Content -Path "$TerraformDir\terraform.tfvars" -Encoding UTF8 -Value @"
tags                = {}
resource_group_name = "$ResourceGroup"
environment         = "$GitHubEnv"
base_name           = "$BaseName"
location            = "$Location"

# Foundry (AI Services)
cognitive_account_kind       = "AIServices"
oai_sku_name                 = "S0"
oai_deployment_sku_name      = "GlobalStandard"
oai_deployment_model_format  = "OpenAI"
oai_deployment_model_name    = "gpt-4.1"
oai_deployment_model_version = "2025-04-14"
oai_version_upgrade_option   = "NoAutoUpgrade"
create_embedding_deployment  = true
embedding_model_name         = "text-embedding-ada-002"
embedding_model_version      = "2"
embedding_sku_name           = "Standard"
embedding_capacity           = 10

# Cosmos DB (1 account: agents session state)
cosmos_project_name               = "$BaseName"
cosmos_agents_database_name       = "agents"
cosmos_agent_state_container_name = "workshop_agent_state_store"

# Azure SQL Database (CRM operational data)
sql_database_name = "contoso-outdoors"
sql_admin_login   = "sqladmin"

# Storage
storage_project_name = "$BaseName"

# AI Search
search_sku        = "basic"
search_index_name = "knowledge-documents"

# ACR
acr_project_name  = "$BaseName"
create_acr        = true
acr_sku           = "Premium"
existing_acr_name = ""

# AKS
aks_kubernetes_version   = null
aks_node_vm_size         = "Standard_D4s_v5"
aks_node_count           = 2
aks_auto_scaling_enabled = true
aks_node_min_count       = 1
aks_node_max_count       = 5
aks_os_disk_size_gb      = 64
aks_log_retention_days   = 30
"@
Write-Done "terraform.tfvars"

# ═══════════════════════════════════════════════════════════════════════════════

Write-Host ""
Write-Host "  ╔═══════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "  ║  Bootstrap Complete!                                  ║" -ForegroundColor Green
Write-Host "  ╠═══════════════════════════════════════════════════════╣" -ForegroundColor Green
Write-Host "  ║" -ForegroundColor Green -NoNewLine; Write-Host "  Subscription:     $SubName"
Write-Host "  ║" -ForegroundColor Green -NoNewLine; Write-Host "  Location:         $Location"
Write-Host "  ║" -ForegroundColor Green -NoNewLine; Write-Host "  Resource group:   $ResourceGroup"
Write-Host "  ║" -ForegroundColor Green -NoNewLine; Write-Host "  Storage account:  $StorageAccount"
Write-Host "  ║" -ForegroundColor Green -NoNewLine; Write-Host "  App registration: $AppClientId"
Write-Host "  ║" -ForegroundColor Green -NoNewLine; Write-Host "  GitHub repo:      $GitHubRepo"
Write-Host "  ║" -ForegroundColor Green -NoNewLine; Write-Host "  GitHub env:       $GitHubEnv"
Write-Host "  ║" -ForegroundColor Green -NoNewLine; Write-Host "  Secrets:          3"
Write-Host "  ║" -ForegroundColor Green -NoNewLine; Write-Host "  Env variables:    $($envVars.Count)"
Write-Host "  ║" -ForegroundColor Green
Write-Host "  ║  Next: proceed to Lab 1 (terraform apply)             ║" -ForegroundColor Green
Write-Host "  ╚═══════════════════════════════════════════════════════╝" -ForegroundColor Green
Write-Host ""
