#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    One-time bootstrap: creates Azure backend resources, Entra app registration
    with OIDC for GitHub Actions, and configures GitHub repository secrets + variables.

.DESCRIPTION
    This script performs all Lab 0 setup in a single execution:
    1. Generates terraform.tfvars and backend.hcl (if not exist)
    2. Creates Azure resource group, storage account, and blob container (Terraform backend)
    3. Creates an Entra app registration + service principal with OIDC federation
    4. Grants Contributor role on the subscription
    5. Sets GitHub repository secrets and environment variables
    6. Disables public network access on the state storage account

    All GitHub environment variables are read from the generated terraform.tfvars —
    no hardcoded values.

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
    ./init.ps1
    ./init.ps1 -SubscriptionId "12345678-..." -GitHubRepo "myorg/myrepo"
    ./init.ps1 -SkipEntra -AppClientId "12345678-..."
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

$ScriptDir    = $PSScriptRoot
$TerraformDir = "$ScriptDir\terraform"

# ── Helpers ──────────────────────────────────────────────────────────────────
function Write-Step  { param([string]$Message) Write-Host "`n==> $Message" -ForegroundColor Cyan }
function Write-Done  { param([string]$Message) Write-Host "    ✓ $Message" -ForegroundColor Green }
function Write-Skip  { param([string]$Message) Write-Host "    ⊘ $Message" -ForegroundColor Yellow }

function Get-HclValue {
    param([string]$File, [string]$Key)
    $line = Get-Content $File | Where-Object { $_ -match "^\s*$Key\s*=" } | Select-Object -First 1
    if ($line -match '=\s*"(.+?)"') { return $Matches[1] }
    if ($line -match '=\s*(\S+)') { return $Matches[1] }
    throw "Could not find key '$Key' in $File"
}

# ── Verify prerequisites ────────────────────────────────────────────────────
Write-Step "Checking prerequisites"

foreach ($cmd in @("az", "gh", "terraform", "dotnet")) {
    if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
        throw "$cmd is not installed. See the prerequisites in docs/lab-0.md."
    }
}

Write-Done "az, gh, terraform, dotnet available"

# ── Authenticate ───────────────────────────────────────────────────────────────
Write-Step "Authenticating"

Write-Host "    Signing in to Azure CLI..."
az login | Out-Null
$acct = az account show --query "{name:name, id:id}" -o tsv
Write-Done "Azure: $acct"

Write-Host "    Signing in to GitHub CLI..."
try { $null = gh auth status 2>&1; if ($LASTEXITCODE -ne 0) { throw } }
catch {
    gh auth login
}
$ghUser = gh api user --jq .login 2>$null
Write-Done "GitHub: $ghUser"

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 1 — Generate config files
# ═══════════════════════════════════════════════════════════════════════════════

Write-Step "Generating configuration files"

# ── terraform.tfvars ─────────────────────────────────────────────────────────
$TfVarsFile = "$TerraformDir\terraform.tfvars"

if (-not (Test-Path $TfVarsFile)) {
    $tfvarsContent = @'
tags                = {}
resource_group_name = "rg-dotnetagent-dev-centralus"

environment = "dev"
base_name   = "dotnetagent"
location    = "centralus"

# ---------------------------------------------------------------
# Foundry (AI Services)
# ---------------------------------------------------------------
cognitive_account_kind       = "AIServices"
oai_sku_name                 = "S0"
oai_deployment_sku_name      = "GlobalStandard"
oai_deployment_model_format  = "OpenAI"
oai_deployment_model_name    = "gpt-4.1"
oai_deployment_model_version = "2025-04-14"
oai_version_upgrade_option   = "NoAutoUpgrade"

create_embedding_deployment = true
embedding_model_name        = "text-embedding-ada-002"
embedding_model_version     = "2"
embedding_sku_name          = "Standard"
embedding_capacity          = 10

# ---------------------------------------------------------------
# Cosmos DB (1 account: agents session state)
# ---------------------------------------------------------------
cosmos_project_name               = "dotnetagent"
cosmos_agents_database_name       = "agents"
cosmos_agent_state_container_name = "workshop_agent_state_store"

# ---------------------------------------------------------------
# Azure SQL Database (CRM operational data)
# ---------------------------------------------------------------
sql_database_name = "contoso-outdoors"
sql_admin_login   = "sqladmin"

# ---------------------------------------------------------------
# Storage
# ---------------------------------------------------------------
storage_project_name = "dotnetagent"

# ---------------------------------------------------------------
# AI Search
# ---------------------------------------------------------------
search_sku        = "basic"
search_index_name = "knowledge-documents"

# ---------------------------------------------------------------
# ACR
# ---------------------------------------------------------------
acr_project_name  = "dotnetagent"
create_acr        = true
acr_sku           = "Premium"
existing_acr_name = ""

# ---------------------------------------------------------------
# AKS
# ---------------------------------------------------------------
aks_kubernetes_version   = null
aks_node_vm_size         = "Standard_D4s_v5"
aks_node_count           = 2
aks_auto_scaling_enabled = true
aks_node_min_count       = 1
aks_node_max_count       = 5
aks_os_disk_size_gb      = 64
aks_log_retention_days   = 30
'@
    Set-Content -Path $TfVarsFile -Value $tfvarsContent -Encoding UTF8
    Write-Done "Generated $TfVarsFile with default values"
    Write-Host "         (Edit this file to customize resource names, regions, or SKUs)"
} else {
    Write-Skip "Using existing $TfVarsFile"
}

# ── Read values from terraform.tfvars ────────────────────────────────────────
$ResourceGroup = Get-HclValue -File $TfVarsFile -Key "resource_group_name"
$Location      = Get-HclValue -File $TfVarsFile -Key "location"
$Environment   = Get-HclValue -File $TfVarsFile -Key "environment"

# ── backend.hcl ─────────────────────────────────────────────────────────────
$BackendHcl = "$TerraformDir\backend.hcl"

if (-not (Test-Path $BackendHcl)) {
    $sanitized = ($ResourceGroup -replace '^rg-', '' -replace '[^a-z0-9]', '').ToLower()
    $StorageAccount = "st$sanitized"
    if ($StorageAccount.Length -gt 24) { $StorageAccount = $StorageAccount.Substring(0, 24) }

    $hclContent = @"
resource_group_name  = "$ResourceGroup"
storage_account_name = "$StorageAccount"
container_name       = "tfstate"
key                  = "$Environment.tfstate"
"@
    Set-Content -Path $BackendHcl -Value $hclContent -Encoding UTF8
    Write-Done "Generated $BackendHcl (storage account: $StorageAccount)"
} else {
    Write-Skip "Using existing $BackendHcl"
}

$StorageAccount = Get-HclValue -File $BackendHcl -Key "storage_account_name"
$ContainerName  = Get-HclValue -File $BackendHcl -Key "container_name"

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 2 — Create Azure backend resources
# ═══════════════════════════════════════════════════════════════════════════════

Write-Step "Creating Terraform backend resources"

Write-Host ""
Write-Host "    Resource Group:   $ResourceGroup"
Write-Host "    Storage Account:  $StorageAccount"
Write-Host "    Container:        $ContainerName"
Write-Host "    Location:         $Location"
Write-Host ""

# ── Resource group ───────────────────────────────────────────────────────────
$null = az group show --name $ResourceGroup 2>&1
if ($LASTEXITCODE -ne 0) {
    az group create --name $ResourceGroup --location $Location | Out-Null
    Write-Done "Created resource group $ResourceGroup"
} else {
    Write-Skip "Resource group $ResourceGroup already exists"
}

# ── Storage account + container ──────────────────────────────────────────────
$WaitTime = 30

$null = az storage account show --resource-group $ResourceGroup --name $StorageAccount 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "    Creating storage account $StorageAccount..."
    az storage account create `
        --resource-group $ResourceGroup `
        --name $StorageAccount `
        --sku "Standard_LRS" `
        --encryption-services blob `
        --min-tls-version "TLS1_2" `
        --location $Location `
        --public-network-access Enabled | Out-Null

    Write-Host "    Waiting ${WaitTime}s for storage account to be ready..."
    Start-Sleep -Seconds $WaitTime
    Write-Done "Created storage account $StorageAccount"
} else {
    Write-Skip "Storage account $StorageAccount already exists"
    az storage account update --name $StorageAccount --resource-group $ResourceGroup --public-network-access Enabled | Out-Null
    Write-Host "    Enabled public network access. Waiting ${WaitTime}s..."
    Start-Sleep -Seconds $WaitTime
}

$null = az storage container show --name $ContainerName --account-name $StorageAccount --auth-mode login 2>&1
if ($LASTEXITCODE -ne 0) {
    az storage container create --name $ContainerName --account-name $StorageAccount --auth-mode login | Out-Null
    Write-Done "Created container $ContainerName"
} else {
    Write-Skip "Container $ContainerName already exists"
}

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 3 — Entra app registration + OIDC
# ═══════════════════════════════════════════════════════════════════════════════

# ── Resolve defaults ─────────────────────────────────────────────────────────
if (-not $SubscriptionId) {
    $SubscriptionId = (az account show --query id -o tsv)
}
if (-not $GitHubRepo) {
    $GitHubRepo = (gh repo view --json nameWithOwner -q ".nameWithOwner" 2>$null)
    if (-not $GitHubRepo) { throw "Could not detect GitHub repo. Pass -GitHubRepo 'owner/repo'." }
}
$RepoName = ($GitHubRepo -split "/")[-1]
if (-not $AppName) { $AppName = "github-actions-$RepoName" }
$TenantId = (az account show --query tenantId -o tsv)

if ($SkipEntra) {
    if (-not $AppClientId) { throw "-SkipEntra requires -AppClientId" }
    Write-Skip "Skipping Entra setup (using existing app: $AppClientId)"
} else {
    Write-Step "Creating Entra app registration"

    $existing = az ad app list --display-name "$AppName" --query "[0].appId" -o tsv 2>$null
    if ($existing) {
        $AppClientId = $existing
        Write-Skip "App '$AppName' already exists: $AppClientId"
    } else {
        $AppClientId = az ad app create --display-name "$AppName" --query appId -o tsv
        Write-Done "Created app: $AppClientId"
    }

    # Service principal
    $spExists = az ad sp show --id "$AppClientId" --query id -o tsv 2>$null
    if ($spExists) {
        Write-Skip "Service principal already exists"
    } else {
        $null = az ad sp create --id "$AppClientId"
        Write-Done "Created service principal"
    }

    # OIDC federated credential
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

    # Contributor role
    Write-Step "Granting Contributor role on subscription"
    $roleExists = az role assignment list --assignee "$AppClientId" --role "Contributor" --scope "/subscriptions/$SubscriptionId" --query "[0].id" -o tsv 2>$null
    if ($roleExists) {
        Write-Skip "Contributor role already assigned"
    } else {
        $null = az role assignment create --assignee "$AppClientId" --role "Contributor" --scope "/subscriptions/$SubscriptionId"
        Write-Done "Granted Contributor on subscription $SubscriptionId"
    }
}

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 4 — GitHub secrets + environment variables
# ═══════════════════════════════════════════════════════════════════════════════

Write-Step "Setting GitHub repository secrets"

gh secret set AZURE_CLIENT_ID --repo "$GitHubRepo" --body "$AppClientId"
Write-Done "AZURE_CLIENT_ID"
gh secret set AZURE_TENANT_ID --repo "$GitHubRepo" --body "$TenantId"
Write-Done "AZURE_TENANT_ID"
gh secret set AZURE_SUBSCRIPTION_ID --repo "$GitHubRepo" --body "$SubscriptionId"
Write-Done "AZURE_SUBSCRIPTION_ID"

Write-Step "Setting GitHub environment variables ($GitHubEnv)"

# Read all values from terraform.tfvars — single source of truth
$envVars = [ordered]@{
    # Backend / bootstrap
    RESOURCE_GROUP                        = $ResourceGroup
    LOCATION                              = $Location
    STORAGE_ACCOUNT                       = $StorageAccount
    STORAGE_ACCOUNT_SKU                   = "Standard_LRS"
    STORAGE_ACCOUNT_ENCRYPTION_SERVICES   = "blob"
    STORAGE_ACCOUNT_MIN_TLS_VERSION       = "TLS1_2"
    STORAGE_ACCOUNT_PUBLIC_NETWORK_ACCESS = "Enabled"
    TERRAFORM_STATE_CONTAINER             = $ContainerName
    TERRAFORM_STATE_BLOB                  = "$Environment.tfstate"
    TERRAFORM_WORKING_DIRECTORY           = "infra/terraform"

    # Infrastructure (mapped to TF_VAR_ in workflows)
    TAGS                            = "{}"
    ENVIRONMENT                     = $Environment
    BASE_NAME                       = (Get-HclValue -File $TfVarsFile -Key "base_name")
    COGNITIVE_ACCOUNT_KIND          = (Get-HclValue -File $TfVarsFile -Key "cognitive_account_kind")
    OAI_SKU_NAME                    = (Get-HclValue -File $TfVarsFile -Key "oai_sku_name")
    OAI_DEPLOYMENT_SKU_NAME         = (Get-HclValue -File $TfVarsFile -Key "oai_deployment_sku_name")
    OAI_DEPLOYMENT_MODEL_FORMAT     = (Get-HclValue -File $TfVarsFile -Key "oai_deployment_model_format")
    OAI_DEPLOYMENT_MODEL_NAME       = (Get-HclValue -File $TfVarsFile -Key "oai_deployment_model_name")
    OAI_DEPLOYMENT_MODEL_VERSION    = (Get-HclValue -File $TfVarsFile -Key "oai_deployment_model_version")
    OAI_VERSION_UPGRADE_OPTION      = (Get-HclValue -File $TfVarsFile -Key "oai_version_upgrade_option")
    CREATE_EMBEDDING_DEPLOYMENT     = (Get-HclValue -File $TfVarsFile -Key "create_embedding_deployment")
    EMBEDDING_MODEL_NAME            = (Get-HclValue -File $TfVarsFile -Key "embedding_model_name")
    EMBEDDING_MODEL_VERSION         = (Get-HclValue -File $TfVarsFile -Key "embedding_model_version")
    EMBEDDING_SKU_NAME              = (Get-HclValue -File $TfVarsFile -Key "embedding_sku_name")
    EMBEDDING_CAPACITY              = (Get-HclValue -File $TfVarsFile -Key "embedding_capacity")
    COSMOS_PROJECT_NAME             = (Get-HclValue -File $TfVarsFile -Key "cosmos_project_name")
    COSMOS_AGENTS_DATABASE_NAME     = (Get-HclValue -File $TfVarsFile -Key "cosmos_agents_database_name")
    COSMOS_AGENT_STATE_CONTAINER_NAME = (Get-HclValue -File $TfVarsFile -Key "cosmos_agent_state_container_name")
    SQL_DATABASE_NAME               = (Get-HclValue -File $TfVarsFile -Key "sql_database_name")
    SQL_ADMIN_LOGIN                 = (Get-HclValue -File $TfVarsFile -Key "sql_admin_login")
    STORAGE_PROJECT_NAME            = (Get-HclValue -File $TfVarsFile -Key "storage_project_name")
    SEARCH_SKU                      = (Get-HclValue -File $TfVarsFile -Key "search_sku")
    SEARCH_INDEX_NAME               = (Get-HclValue -File $TfVarsFile -Key "search_index_name")
    ACR_PROJECT_NAME                = (Get-HclValue -File $TfVarsFile -Key "acr_project_name")
    CREATE_ACR                      = (Get-HclValue -File $TfVarsFile -Key "create_acr")
    ACR_SKU                         = (Get-HclValue -File $TfVarsFile -Key "acr_sku")
    EXISTING_ACR_NAME               = (Get-HclValue -File $TfVarsFile -Key "existing_acr_name")
    AKS_KUBERNETES_VERSION          = ""
    AKS_NODE_VM_SIZE                = (Get-HclValue -File $TfVarsFile -Key "aks_node_vm_size")
    AKS_NODE_COUNT                  = (Get-HclValue -File $TfVarsFile -Key "aks_node_count")
    AKS_AUTO_SCALING_ENABLED        = (Get-HclValue -File $TfVarsFile -Key "aks_auto_scaling_enabled")
    AKS_NODE_MIN_COUNT              = (Get-HclValue -File $TfVarsFile -Key "aks_node_min_count")
    AKS_NODE_MAX_COUNT              = (Get-HclValue -File $TfVarsFile -Key "aks_node_max_count")
    AKS_OS_DISK_SIZE_GB             = (Get-HclValue -File $TfVarsFile -Key "aks_os_disk_size_gb")
    AKS_LOG_RETENTION_DAYS          = (Get-HclValue -File $TfVarsFile -Key "aks_log_retention_days")
}

foreach ($kv in $envVars.GetEnumerator()) {
    gh variable set $kv.Key --repo "$GitHubRepo" --env "$GitHubEnv" --body "$($kv.Value)"
}

Write-Done "Set $($envVars.Count) environment variables"

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 5 — Lock down storage
# ═══════════════════════════════════════════════════════════════════════════════

Write-Step "Disabling public network access on state storage"

az storage account update --name $StorageAccount --resource-group $ResourceGroup --public-network-access Disabled | Out-Null
Write-Done "Public access disabled on $StorageAccount"

# ═══════════════════════════════════════════════════════════════════════════════
# Summary
# ═══════════════════════════════════════════════════════════════════════════════

Write-Host ""
Write-Host "╔═══════════════════════════════════════════════════════════╗"
Write-Host "║  Bootstrap Complete                                      ║"
Write-Host "╠═══════════════════════════════════════════════════════════╣"
Write-Host "║  Resource group:   $ResourceGroup"
Write-Host "║  Storage account:  $StorageAccount"
Write-Host "║  App registration: $AppClientId"
Write-Host "║  GitHub repo:      $GitHubRepo"
Write-Host "║  GitHub env:       $GitHubEnv"
Write-Host "║  Repo secrets:     3 (AZURE_CLIENT_ID, TENANT_ID, SUBSCRIPTION_ID)"
Write-Host "║  Env variables:    $($envVars.Count)"
Write-Host "╚═══════════════════════════════════════════════════════════╝"
Write-Host ""
Write-Host "Next steps: proceed to Lab 1"
