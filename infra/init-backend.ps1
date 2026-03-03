#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ScriptDir = $PSScriptRoot

#-------------------------------------------------------
# Environment selection
#-------------------------------------------------------
$Environments = @(
    @{ Name = "agentic-ai"; Path = "$ScriptDir\terraform" }
)

Write-Host ""
Write-Host "Select the environment to bootstrap:"
Write-Host ""
for ($i = 0; $i -lt $Environments.Count; $i++) {
    Write-Host "  [$($i + 1)] $($Environments[$i].Name)"
}
Write-Host ""

$Selection = Read-Host "Enter selection (1-$($Environments.Count))"
$Index = [int]$Selection - 1

if ($Index -lt 0 -or $Index -ge $Environments.Count) {
    Write-Error "Invalid selection."
    exit 1
}

$SelectedEnv  = $Environments[$Index]
$TerraformDir = $SelectedEnv.Path

Write-Host ""
Write-Host "Bootstrapping backend for: $($SelectedEnv.Name)"
Write-Host ""

#-------------------------------------------------------
# Parse values from backend.hcl and terraform.tfvars
#-------------------------------------------------------
function Get-HclValue {
    param([string]$File, [string]$Key)
    $line = Get-Content $File | Where-Object { $_ -match "^\s*$Key\s*=" } | Select-Object -First 1
    if ($line -match '=\s*"(.+?)"') { return $Matches[1] }
    throw "Could not find key '$Key' in $File"
}

$ResourceGroup  = Get-HclValue -File "$TerraformDir\backend.hcl" -Key "resource_group_name"
$StorageAccount = Get-HclValue -File "$TerraformDir\backend.hcl" -Key "storage_account_name"
$ContainerName  = Get-HclValue -File "$TerraformDir\backend.hcl" -Key "container_name"
$Location       = Get-HclValue -File "$TerraformDir\terraform.tfvars" -Key "location"

# Storage account defaults (match CI/CD workflow)
$StorageAccountSku               = "Standard_LRS"
$StorageAccountEncryptionServices = "blob"
$StorageAccountMinTlsVersion     = "TLS1_2"

Write-Host "=== Terraform Backend Bootstrap ==="
Write-Host "Environment:      $($SelectedEnv.Name)"
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
