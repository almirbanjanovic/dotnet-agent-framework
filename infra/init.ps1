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
    5. Generates <env>.tfvars and backend.hcl

.EXAMPLE
    ./init.ps1
#>

$SubscriptionId = ""
$GitHubEnv      = "dev"
$Location       = "eastus2"
$BaseName       = "dotnetagent"

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
    Write-Host "  ║     5. Config files (<env>.tfvars, backend.hcl)       ║" -ForegroundColor DarkCyan
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
    param([int]$Number, [hashtable]$Items, [string]$NextPhase)
    
    # Build content lines and find max width
    $header = " Phase $Number complete "
    $lines = @()
    foreach ($kv in $Items.GetEnumerator()) {
        $lines += "  $($kv.Key): $($kv.Value)"
    }
    $maxLen = ($lines | ForEach-Object { $_.Length }) | Measure-Object -Maximum | Select-Object -ExpandProperty Maximum
    $maxLen = [Math]::Max($maxLen, $header.Length)
    $boxWidth = $maxLen + 2  # padding

    $topFill = '─' * ($boxWidth - $header.Length)
    $botFill = '─' * $boxWidth

    Write-Host ""
    Write-Host "    ┌${header}${topFill}┐" -ForegroundColor Green
    foreach ($line in $lines) {
        $pad = ' ' * ($boxWidth - $line.Length)
        Write-Host "    │" -ForegroundColor Green -NoNewLine
        Write-Host "${line}${pad}" -NoNewLine
        Write-Host "│" -ForegroundColor Green
    }
    Write-Host "    └${botFill}┘" -ForegroundColor Green
    if ($NextPhase) {
        Write-Host "    Next: " -NoNewLine -ForegroundColor DarkGray
        Write-Host "$NextPhase" -ForegroundColor Cyan
    }
    $response = Read-Host "    Continue? (Y/n)"
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

# Disable WAM broker — prevents the gray popup issue on Windows
az config set core.enable_broker_on_windows=false 2>$null
# Disable interactive subscription picker — prevents az login from hanging
az config set core.login_experience_v2=off 2>$null

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 1 — Authenticate
# ═══════════════════════════════════════════════════════════════════════════════

Write-Phase -Number 1 -Title "Authenticate"

# ── Azure ────────────────────────────────────────────────────────────────────
Write-Step "Signing in to Azure"

Write-Host "    A browser tab will open — select the correct account." -ForegroundColor DarkGray
# Request Graph scope upfront to avoid stale-token errors (TokenCreatedWithOutdatedPolicies)
# during Entra operations in Phase 2.
az login --scope https://graph.microsoft.com/.default | Out-Null

if (-not $SubscriptionId) {
    $subs = az account list --query "[].{name:name, id:id, isDefault:isDefault}" -o json | ConvertFrom-Json
    $currentId = az account show --query id -o tsv
    Write-Host ""
    Write-Host "    Available subscriptions:" -ForegroundColor DarkGray
    Write-Host ""
    for ($i = 0; $i -lt $subs.Count; $i++) {
        $marker = if ($subs[$i].id -eq $currentId) { "*" } else { " " }
        $color  = if ($subs[$i].id -eq $currentId) { "Cyan" } else { "White" }
        Write-Host "    $marker $($i + 1). " -ForegroundColor $color -NoNewLine
        Write-Host "$($subs[$i].name)" -ForegroundColor $color
        Write-Host "         $($subs[$i].id)" -ForegroundColor DarkGray
    }
    Write-Host ""
    Write-Host "    * = current default" -ForegroundColor DarkGray
    Write-Host ""
    $pick = Read-Host "    Select subscription [1-$($subs.Count), or press Enter for current]"
    if ($pick -and $pick -match '^\d+$') {
        $idx = [int]$pick - 1
        if ($idx -ge 0 -and $idx -lt $subs.Count) {
            $SubscriptionId = $subs[$idx].id
            az account set --subscription $SubscriptionId
        } else {
            throw "Invalid selection: $pick"
        }
    } else {
        $SubscriptionId = $currentId
    }
} else {
    az account set --subscription $SubscriptionId
}

$SubName  = az account show --query name -o tsv
$TenantId = az account show --query tenantId -o tsv
Write-Done "Azure: $SubName ($SubscriptionId)"

# ── Deployment mode ──────────────────────────────────────────────────────────
Write-Host ""
Write-Host "    ┌─────────────────────────────────────────────────────────┐" -ForegroundColor Cyan
Write-Host "    │  How would you like to deploy?                          │" -ForegroundColor Cyan
Write-Host "    │                                                         │" -ForegroundColor Cyan
Write-Host "    │    1. Full setup — Azure + GitHub Actions CI/CD         │" -ForegroundColor Cyan
Write-Host "    │       (Entra OIDC app, GitHub secrets, variables)       │" -ForegroundColor Cyan
Write-Host "    │                                                         │" -ForegroundColor Cyan
Write-Host "    │    2. Local only — Azure backend only                   │" -ForegroundColor Cyan
Write-Host "    │       (Deploy with ./deploy.ps1 or ./deploy.sh)         │" -ForegroundColor Cyan
Write-Host "    └─────────────────────────────────────────────────────────┘" -ForegroundColor Cyan
Write-Host ""
$modeChoice = Read-Host "    Select [1-2, or press Enter for full setup]"
$LocalOnly = ($modeChoice -eq '2')

if ($LocalOnly) {
    Write-Done "Mode: Local only (GitHub CI/CD will be skipped)"
} else {
    Write-Done "Mode: Full setup (Azure + GitHub CI/CD)"

    # Check GitHub CLI prerequisite
    if (Get-Command gh -ErrorAction SilentlyContinue) {
        Write-Done "GitHub CLI (gh)"
    } else {
        Write-Host "    Installing GitHub CLI..." -ForegroundColor Yellow
        if (Get-Command winget -ErrorAction SilentlyContinue) {
            winget install --id GitHub.cli --accept-source-agreements --accept-package-agreements | Out-Null
            $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")
            if (Get-Command gh -ErrorAction SilentlyContinue) {
                Write-Done "GitHub CLI installed"
            } else {
                throw "Failed to install GitHub CLI. Install manually and re-run."
            }
        } else {
            throw "GitHub CLI is not installed and winget is not available. Install manually: see docs/lab-0.md"
        }
    }
}

if (-not $LocalOnly) {

$ghStatus = gh auth status 2>&1
if ($ghStatus -match "Logged in") {
    Write-Skip "Already logged in to GitHub"
} else {
    Write-Host "    Run this command in a separate terminal if the browser flow hangs:" -ForegroundColor DarkGray
    Write-Host "      gh auth login --hostname github.com --git-protocol https --web" -ForegroundColor DarkGray
    Write-Host ""
    gh auth login --hostname github.com --git-protocol https --web
}

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

} # end if (-not $LocalOnly)

# ── Environment ──────────────────────────────────────────────────────────────
Write-Step "Select environment"
Write-Host ""
Write-Host "    Choose an environment for this deployment:" -ForegroundColor DarkGray
Write-Host "      1. dev       (development — default)" -ForegroundColor DarkGray
Write-Host "      2. staging   (pre-production)" -ForegroundColor DarkGray
Write-Host "      3. prod      (production)" -ForegroundColor DarkGray
Write-Host "      4. custom    (enter your own name)" -ForegroundColor DarkGray
Write-Host ""
$envChoice = Read-Host "    Select [1-4, or press Enter for dev]"

switch ($envChoice) {
    "2" { $GitHubEnv = "staging" }
    "3" { $GitHubEnv = "prod" }
    "4" { $GitHubEnv = Read-Host "    Enter environment name" }
    default { } # keep the parameter default or what was passed
}
Write-Done "Environment: $GitHubEnv"

# ── Recalculate derived names with final values ──────────────────────────────
$ResourceGroup  = "rg-$BaseName-$GitHubEnv-$Location"
$StorageAccount = ("st" + ($ResourceGroup -replace '^rg-', '' -replace '[^a-z0-9]', '').ToLower())
if ($StorageAccount.Length -gt 24) { $StorageAccount = $StorageAccount.Substring(0, 24) }

# ── Show configuration & confirm ─────────────────────────────────────────────
$phase1Items = [ordered]@{
    "Subscription"   = "$SubName ($SubscriptionId)"
    "Tenant"         = $TenantId
    "Location"       = $Location
    "Base name"      = $BaseName
    "Environment"    = $GitHubEnv
    "Resource group" = $ResourceGroup
    "Deploy mode"    = if ($LocalOnly) { "Local only" } else { "Full (Azure + GitHub)" }
}
if (-not $LocalOnly) { $phase1Items["GitHub repo"] = $GitHubRepo }

$nextPhase = if ($LocalOnly) { "Phase 4 — Create Azure resource group, storage account, blob container" } else { "Phase 2 — Create Entra app registration, service principal, and OIDC federated credential" }
Write-PhaseSummary -Number 1 -NextPhase $nextPhase -Items $phase1Items

if (-not $LocalOnly) {

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 2 — Entra app registration + OIDC + RBAC
# ═══════════════════════════════════════════════════════════════════════════════

Write-Phase -Number 2 -Title "Entra app registration + OIDC + RBAC"

$AppName = "github-actions-$RepoName"

Write-Step "Creating app registration"
$existing = az ad app list --display-name "$AppName" --query "[0].appId" -o tsv 2>$null
if ($existing) {
    $AppClientId = $existing
    Write-Skip "App '$AppName' already exists: $AppClientId"
} else {
    $AppClientId = az ad app create --display-name "$AppName" --query appId -o tsv 2>$null
    if (-not $AppClientId) {
        # Retry once — CAE challenge can occur if Entra policies changed since login.
        # Must clear cached tokens and do a full interactive login to satisfy the challenge.
        Write-Host "    ⚠ Entra operation failed. Clearing cached tokens and re-authenticating..." -ForegroundColor Yellow
        az account clear 2>$null
        az login --scope https://graph.microsoft.com/.default --tenant $TenantId
        az account set --subscription $SubscriptionId
        $AppClientId = az ad app create --display-name "$AppName" --query appId -o tsv
        if (-not $AppClientId) {
            throw "Failed to create app registration. Check your permissions."
        }
    }
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
    $credFile = [System.IO.Path]::GetTempFileName()
    @{
        name        = $credName
        issuer      = "https://token.actions.githubusercontent.com"
        subject     = "repo:${GitHubRepo}:environment:${GitHubEnv}"
        audiences   = @("api://AzureADTokenExchange")
        description = "GitHub Actions OIDC for $RepoName ($GitHubEnv)"
    } | ConvertTo-Json | Set-Content -Path $credFile -Encoding UTF8
    $null = az ad app federated-credential create --id "$AppClientId" --parameters "@$credFile"
    Remove-Item $credFile
    Write-Done "Federated credential for repo:${GitHubRepo}:environment:${GitHubEnv}"
}

Write-PhaseSummary -Number 2 -NextPhase "Phase 3 — Create GitHub environment, set repository secrets and environment variables" -Items ([ordered]@{
    "App registration" = "$AppName ($AppClientId)"
    "OIDC subject"     = "repo:${GitHubRepo}:environment:${GitHubEnv}"
    "Credential name"  = $credName
    "RBAC"             = "Contributor on $ResourceGroup (granted in Phase 4)"
})

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 3 — GitHub environment, secrets, variables

Write-Phase -Number 3 -Title "GitHub environment, secrets, variables"

# ── Create environment first ─────────────────────────────────────────────────
Write-Step "Creating GitHub environment '$GitHubEnv'"
$null = gh api --method PUT "repos/$GitHubRepo/environments/$GitHubEnv" 2>$null
Write-Done "Environment '$GitHubEnv' ready"

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
    COSMOS_AGENTS_DATABASE_NAME       = "agents"
    COSMOS_AGENT_STATE_CONTAINER_NAME = "workshop_agent_state_store"
    COSMOS_CRM_DATABASE_NAME          = "contoso-crm"
    SEARCH_SKU                        = "standard"
    SEARCH_INDEX_NAME                 = "knowledge-documents"
    CREATE_ACR                        = "true"
    ACR_SKU                           = "Premium"
    ACR_NAME                          = ("acr" + $BaseName + $GitHubEnv + $Location) -replace '-',''
    AKS_KUBERNETES_VERSION            = "1.34"
    AKS_SYSTEM_NODE_VM_SIZE            = "Standard_D4s_v6"
    AKS_WORKLOAD_NODE_VM_SIZE            = "Standard_D4s_v6"
    AKS_AUTO_SCALING_ENABLED          = "true"
    AKS_OS_DISK_SIZE_GB               = "64"
    AKS_LOG_RETENTION_DAYS            = "30"
}

foreach ($kv in $envVars.GetEnumerator()) {
    $val = if ($kv.Value) { $kv.Value } else { " " }
    gh variable set $kv.Key --repo "$GitHubRepo" --env "$GitHubEnv" --body $val
}
Write-Done "Set $($envVars.Count) environment variables in '$GitHubEnv'"

Write-PhaseSummary -Number 3 -NextPhase "Phase 4 — Create Azure resource group, storage account, blob container, and assign RBAC" -Items ([ordered]@{
    "Secrets"       = "AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_SUBSCRIPTION_ID"
    "Environment"   = $GitHubEnv
    "Env variables" = "$($envVars.Count)"
})

} # end if (-not $LocalOnly) — phases 2 & 3

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
        --default-action Deny | Out-Null
    Write-Host "    Waiting ${WaitTime}s for storage account..."
    Start-Sleep -Seconds $WaitTime
    Write-Done "Created $StorageAccount"
} else {
    Write-Skip "$StorageAccount already exists"
    az storage account update --name $StorageAccount --resource-group $ResourceGroup --default-action Deny -o none 2>$null
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
az storage account update --name $StorageAccount --resource-group $ResourceGroup --default-action Deny -o none 2>$null
Write-Done "Default network action set to Deny"

# ── RBAC: Contributor scoped to resource group (least privilege) ───────────
if (-not $LocalOnly -and $AppClientId) {
    Write-Step "Granting Contributor role on resource group"
    $rgScope = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup"
    $roleExists = az role assignment list --assignee "$AppClientId" --role "Contributor" --scope $rgScope --query "[0].id" -o tsv 2>$null
    if ($roleExists) {
        Write-Skip "Contributor role already assigned on $ResourceGroup"
    } else {
        $null = az role assignment create --assignee "$AppClientId" --role "Contributor" --scope $rgScope
        Write-Done "Contributor granted on $ResourceGroup"
    }

    # ── Graph API: Application.ReadWrite.All for Agent Identity (Entra Agent ID) ──
    Write-Step "Granting Application.ReadWrite.All (Graph API) for Agent Identity"
    $AppRwAllId = "1bfefb4e-e0b5-418b-a88f-73c46d2cc8e9"
    $existingPerm = az ad app permission list --id "$AppClientId" --query "[?resourceAppId=='00000003-0000-0000-c000-000000000000'].resourceAccess[?id=='$AppRwAllId'].id" -o tsv 2>$null
    if ($existingPerm) {
        Write-Skip "Application.ReadWrite.All already granted"
    } else {
        az ad app permission add --id "$AppClientId" --api "00000003-0000-0000-c000-000000000000" --api-permissions "${AppRwAllId}=Role" 2>$null | Out-Null
        Write-Done "Application.ReadWrite.All added"
    }
    az ad app permission admin-consent --id "$AppClientId" 2>$null | Out-Null
    Write-Done "Admin consent applied"
}

$phase4Items = [ordered]@{
    "Resource group"  = $ResourceGroup
    "Storage account" = $StorageAccount
    "Container"       = $ContainerName
    "Public access"   = "Disabled"
}
if (-not $LocalOnly) { $phase4Items["RBAC"] = "Contributor on $ResourceGroup" } else { $phase4Items["RBAC"] = "Skipped (local-only mode)" }

# ── RBAC: Storage Blob Data Contributor for deployer (Terraform state) ────────
Write-Step "Granting Storage Blob Data Contributor to deployer on state storage"
$deployerOid = az ad signed-in-user show --query id -o tsv 2>$null
if ($deployerOid) {
    $stScope = az storage account show --name $StorageAccount --resource-group $ResourceGroup --query id -o tsv 2>$null
    if ($stScope) {
        $blobRole = "Storage Blob Data Contributor"
        $exists = az role assignment list --assignee $deployerOid --role $blobRole --scope $stScope --query "[0].id" -o tsv 2>$null
        if ($exists) {
            Write-Skip "$blobRole already assigned"
        } else {
            az role assignment create --assignee-object-id $deployerOid --assignee-principal-type User --role $blobRole --scope $stScope 2>$null | Out-Null
            Write-Done "$blobRole granted on $StorageAccount"
        }
        $phase4Items["Storage RBAC"] = "Blob Data Contributor on $StorageAccount"
    } else {
        Write-Host "    ⚠ Could not find storage account $StorageAccount — assign Storage Blob Data Contributor manually" -ForegroundColor Yellow
        $phase4Items["Storage RBAC"] = "Manual assignment required"
    }
} else {
    Write-Host "    ⚠ Could not determine deployer identity — assign roles manually" -ForegroundColor Yellow
}

# ── RBAC: Key Vault data-plane roles for deployer (current user) ─────────────
Write-Step "Granting Key Vault data-plane roles to deployer"
if ($deployerOid) {
    $rgScope = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup"
    $kvRoles = @("Key Vault Secrets Officer", "Key Vault Certificates Officer")
    foreach ($role in $kvRoles) {
        $exists = az role assignment list --assignee $deployerOid --role $role --scope $rgScope --query "[0].id" -o tsv 2>$null
        if ($exists) {
            Write-Skip "$role already assigned"
        } else {
            az role assignment create --assignee-object-id $deployerOid --assignee-principal-type User --role $role --scope $rgScope 2>$null | Out-Null
            Write-Done "$role granted on $ResourceGroup"
        }
    }
    $phase4Items["KV RBAC"] = "Secrets Officer + Certificates Officer"
} else {
    Write-Host "    ⚠ Could not determine deployer identity — assign Key Vault roles manually" -ForegroundColor Yellow
    $phase4Items["KV RBAC"] = "Manual assignment required"
}

Write-PhaseSummary -Number 4 -NextPhase "Phase 5 — Generate $GitHubEnv.tfvars and backend.hcl configuration files" -Items $phase4Items

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 5 — Generate config files
# ═══════════════════════════════════════════════════════════════════════════════

Write-Phase -Number 5 -Title "Generate configuration files"

Set-Content -Path "$TerraformDir\backend.hcl" -Encoding UTF8 -Value @"
resource_group_name  = "$ResourceGroup"
storage_account_name = "$StorageAccount"
container_name       = "$ContainerName"
key                  = "$GitHubEnv.tfstate"
use_azuread_auth     = true
"@
Write-Done "backend.hcl"

Set-Content -Path "$TerraformDir\$GitHubEnv.tfvars" -Encoding UTF8 -Value @"
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
cosmos_agents_database_name       = "agents"
cosmos_agent_state_container_name = "workshop_agent_state_store"

# Cosmos DB (CRM operational data)
cosmos_crm_database_name = "contoso-crm"

# AI Search
search_sku        = "standard"
search_index_name = "knowledge-documents"

# ACR
create_acr        = true
acr_sku           = "Premium"
acr_name          = "$(('acr' + $BaseName + $GitHubEnv + $Location) -replace '-','')"

# AKS
aks_kubernetes_version       = "1.34"
aks_system_node_vm_size      = "Standard_D4s_v6"
aks_workload_node_vm_size    = "Standard_D4s_v6"
aks_auto_scaling_enabled     = true
aks_os_disk_size_gb          = 64
aks_log_retention_days       = 30
"@
Write-Done "$GitHubEnv.tfvars"

# ═══════════════════════════════════════════════════════════════════════════════

if ($LocalOnly) {
    Write-Host ""
    Write-Host "  ╔═══════════════════════════════════════════════════════════════╗" -ForegroundColor Green
    Write-Host "  ║  Bootstrap Complete!                                          ║" -ForegroundColor Green
    Write-Host "  ╠═══════════════════════════════════════════════════════════════╣" -ForegroundColor Green
    Write-Host "  ║" -ForegroundColor Green -NoNewLine; Write-Host "  Subscription:     $SubName"
    Write-Host "  ║" -ForegroundColor Green -NoNewLine; Write-Host "  Location:         $Location"
    Write-Host "  ║" -ForegroundColor Green -NoNewLine; Write-Host "  Resource group:   $ResourceGroup"
    Write-Host "  ║" -ForegroundColor Green -NoNewLine; Write-Host "  Storage account:  $StorageAccount"
    Write-Host "  ╠═══════════════════════════════════════════════════════════════╣" -ForegroundColor Green
    Write-Host "  ║" -ForegroundColor Green
    Write-Host "  ║" -ForegroundColor Yellow -NoNewLine; Write-Host "  ⚠  LOCAL-ONLY MODE" -ForegroundColor Yellow
    Write-Host "  ║" -ForegroundColor Yellow -NoNewLine; Write-Host "     GitHub CI/CD was NOT configured" -ForegroundColor Yellow
    Write-Host "  ║" -ForegroundColor Green
    Write-Host "  ║" -ForegroundColor Green -NoNewLine; Write-Host "  Deployments must be run manually:"
    Write-Host "  ║" -ForegroundColor Green -NoNewLine; Write-Host "    cd infra && ./deploy.ps1  (or ./deploy.sh)"
    Write-Host "  ║" -ForegroundColor Green
    Write-Host "  ║" -ForegroundColor Green -NoNewLine; Write-Host "  GitHub Actions workflows will NOT work until"
    Write-Host "  ║" -ForegroundColor Green -NoNewLine; Write-Host "  you re-run this script and select option 1."
    Write-Host "  ║" -ForegroundColor Green
    Write-Host "  ╚═══════════════════════════════════════════════════════════════╝" -ForegroundColor Green
    Write-Host ""
} else {
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
}
