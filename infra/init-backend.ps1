#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ScriptDir    = $PSScriptRoot
$TerraformDir = "$ScriptDir\terraform"

#-------------------------------------------------------
# Parse HCL values helper
#-------------------------------------------------------
function Get-HclValue {
    param([string]$File, [string]$Key)
    $line = Get-Content $File | Where-Object { $_ -match "^\s*$Key\s*=" } | Select-Object -First 1
    if ($line -match '=\s*"(.+?)"') { return $Matches[1] }
    throw "Could not find key '$Key' in $File"
}

#-------------------------------------------------------
# Auto-generate terraform.tfvars if it doesn't exist
#-------------------------------------------------------
$TfVarsFile = "$TerraformDir\terraform.tfvars"

if (-not (Test-Path $TfVarsFile)) {
    $tfvarsContent = @'
tags                = {}
resource_group_name = "rg-agentic-ai-centralus"

environment = "dev"
base_name   = "agentic-ai"
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
# Cosmos DB (3 accounts: operational, knowledge, agents)
# ---------------------------------------------------------------
cosmos_project_name               = "dotnetagent"
cosmos_operational_database_name  = "contoso-outdoors"
cosmos_knowledge_database_name    = "knowledge"
cosmos_agents_database_name       = "agents"
cosmos_agent_state_container_name = "workshop_agent_state_store"

# ---------------------------------------------------------------
# Storage (Product Images)
# ---------------------------------------------------------------
storage_project_name          = "dotnetagent"
storage_images_container_name = "product-images"

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
    Write-Host "Generated $TfVarsFile with default values."
    Write-Host "  (Edit this file to customize resource names, regions, or SKUs)"
    Write-Host ""
} else {
    Write-Host "Using existing $TfVarsFile"
}

$ResourceGroup = Get-HclValue -File $TfVarsFile -Key "resource_group_name"
$Location      = Get-HclValue -File $TfVarsFile -Key "location"

#-------------------------------------------------------
# Auto-generate backend.hcl if it doesn't exist
#-------------------------------------------------------
$BackendHcl = "$TerraformDir\backend.hcl"

if (-not (Test-Path $BackendHcl)) {
    # Derive storage account name: strip hyphens and 'rg-' prefix, keep alphanumeric, prepend 'st'
    $sanitized = ($ResourceGroup -replace '^rg-', '' -replace '[^a-z0-9]', '').ToLower()
    $StorageAccount = "st$sanitized"
    # Storage account names max 24 chars
    if ($StorageAccount.Length -gt 24) { $StorageAccount = $StorageAccount.Substring(0, 24) }

    $Environment = Get-HclValue -File "$TerraformDir\terraform.tfvars" -Key "environment"

    $hclContent = @"
resource_group_name  = "$ResourceGroup"
storage_account_name = "$StorageAccount"
container_name       = "tfstate"
key                  = "$Environment.tfstate"
"@
    Set-Content -Path $BackendHcl -Value $hclContent -Encoding UTF8
    Write-Host "Generated $BackendHcl"
    Write-Host "  Storage account name: $StorageAccount"
    Write-Host "  (Edit this file if you need a different storage account name)"
    Write-Host ""
} else {
    Write-Host "Using existing $BackendHcl"
}

$StorageAccount = Get-HclValue -File $BackendHcl -Key "storage_account_name"
$ContainerName  = Get-HclValue -File $BackendHcl -Key "container_name"

# Storage account defaults (match CI/CD workflow)
$StorageAccountSku               = "Standard_LRS"
$StorageAccountEncryptionServices = "blob"
$StorageAccountMinTlsVersion     = "TLS1_2"

Write-Host ""
Write-Host "=== Terraform Backend Bootstrap ==="
Write-Host "Resource Group:   $ResourceGroup"
Write-Host "Storage Account:  $StorageAccount"
Write-Host "Container:        $ContainerName"
Write-Host "Location:         $Location"
Write-Host "===================================="

#-------------------------------------------------------
# Verify az login
#-------------------------------------------------------
try {
    az account show 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw }
} catch {
    Write-Error "Not logged in to Azure CLI. Run 'az login' first."
    exit 1
}

#-------------------------------------------------------
# Create resource group
#-------------------------------------------------------
$null = az group show --name $ResourceGroup 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "Creating resource group $ResourceGroup..."
    az group create --name $ResourceGroup --location $Location
} else {
    Write-Host "Resource group $ResourceGroup already exists."
}

#-------------------------------------------------------
# Create storage account
#-------------------------------------------------------
$WaitTime = 30

function Disable-PublicAccess {
    Write-Host "Disabling public network access..."
    az storage account update `
        --name $StorageAccount `
        --resource-group $ResourceGroup `
        --public-network-access Disabled
}

try {
    $null = az storage account show --resource-group $ResourceGroup --name $StorageAccount 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Creating storage account $StorageAccount..."
        az storage account create `
            --resource-group $ResourceGroup `
            --name $StorageAccount `
            --sku $StorageAccountSku `
            --encryption-services $StorageAccountEncryptionServices `
            --min-tls-version $StorageAccountMinTlsVersion `
            --location $Location `
            --public-network-access Enabled

        Write-Host "Waiting ${WaitTime}s for storage account to be ready..."
        Start-Sleep -Seconds $WaitTime
    } else {
        Write-Host "Storage account $StorageAccount already exists."

        az storage account update `
            --name $StorageAccount `
            --resource-group $ResourceGroup `
            --public-network-access Enabled

        Write-Host "Enabled public network access. Waiting ${WaitTime}s..."
        Start-Sleep -Seconds $WaitTime
    }

    #-------------------------------------------------------
    # Create blob container
    #-------------------------------------------------------
    $null = az storage container show --name $ContainerName --account-name $StorageAccount --auth-mode login 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Creating container $ContainerName..."
        az storage container create --name $ContainerName --account-name $StorageAccount --auth-mode login
    } else {
        Write-Host "Container $ContainerName already exists."
    }
} finally {
    Disable-PublicAccess
}

Write-Host "Backend bootstrap complete."
