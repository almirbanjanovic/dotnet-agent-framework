<#
.SYNOPSIS
    Local development setup for the .NET Agent Framework.
    Provisions Azure AI Services via Terraform and generates appsettings.Local.json from templates.

.PARAMETER Cleanup
    Destroy Terraform resources and remove generated appsettings.Local.json files.

.EXAMPLE
    .\infra\setup-local.ps1
    .\infra\setup-local.ps1 -Cleanup
#>
[CmdletBinding()]
param(
    [switch]$Cleanup
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$TerraformDir = "infra/terraform/local-dev"

# ── Port Map ────────────────────────────────────────────────────────────────
$PortMap = @(
    @{ Port = 5001; Component = "crm-api" }
    @{ Port = 5002; Component = "crm-mcp" }
    @{ Port = 5003; Component = "knowledge-mcp" }
    @{ Port = 5004; Component = "crm-agent" }
    @{ Port = 5005; Component = "product-agent" }
    @{ Port = 5006; Component = "orchestrator-agent" }
    @{ Port = 5007; Component = "bff-api" }
    @{ Port = 5008; Component = "blazor-ui" }
)

# Templates that need Foundry placeholder replacement
$TemplateComponents = @(
    "simple-agent",
    "knowledge-mcp",
    "crm-agent",
    "product-agent",
    "orchestrator-agent",
    "bff-api",
    "blazor-ui"
)

# Templates that are static (no placeholder replacement needed)
$StaticComponents = @(
    "crm-api",
    "crm-mcp"
)

# ── Helper Functions ────────────────────────────────────────────────────────

function Write-Step {
    param([string]$Message)
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Write-Ok {
    param([string]$Message)
    Write-Host "  [OK] $Message" -ForegroundColor Green
}

function Write-Warn {
    param([string]$Message)
    Write-Host "  [WARN] $Message" -ForegroundColor Yellow
}

function Write-Fail {
    param([string]$Message)
    Write-Host "  [FAIL] $Message" -ForegroundColor Red
}

function Test-Command {
    param([string]$Name)
    $null -ne (Get-Command $Name -ErrorAction SilentlyContinue)
}

# ── Cleanup Mode ────────────────────────────────────────────────────────────

if ($Cleanup) {
    Write-Step "Destroying Terraform resources"
    terraform -chdir="$TerraformDir" destroy -auto-approve -input=false
    if ($LASTEXITCODE -ne 0) {
        Write-Fail "Terraform destroy failed (exit code $LASTEXITCODE)"
        exit 1
    }
    Write-Ok "Terraform resources destroyed"

    Write-Step "Removing generated appsettings.Local.json files"
    $allComponents = $TemplateComponents + $StaticComponents
    foreach ($component in $allComponents) {
        $settingsFile = Join-Path $RepoRoot "src/$component/appsettings.Local.json"
        if (Test-Path $settingsFile) {
            Remove-Item $settingsFile -Force
            Write-Ok "Removed src/$component/appsettings.Local.json"
        }
    }

    Write-Host "`nCleanup complete." -ForegroundColor Green
    exit 0
}

# ── Prerequisites ───────────────────────────────────────────────────────────

Write-Step "Checking prerequisites"

$missing = @()
if (-not (Test-Command "dotnet"))    { $missing += "dotnet" }
if (-not (Test-Command "az"))        { $missing += "az (Azure CLI)" }
if (-not (Test-Command "terraform")) { $missing += "terraform" }

if ($missing.Count -gt 0) {
    Write-Fail "Missing required tools: $($missing -join ', ')"
    Write-Host "  Install them and re-run this script."
    exit 1
}
Write-Ok "dotnet, az, terraform found"

# ── Azure Login Check ──────────────────────────────────────────────────────

Write-Step "Checking Azure login"
$azAccount = az account show 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Fail "Not logged in to Azure. Run 'az login' first."
    exit 1
}
$accountInfo = $azAccount | ConvertFrom-Json
Write-Ok "Logged in as $($accountInfo.user.name) (subscription: $($accountInfo.name))"

# ── Compute Resource Group Name ─────────────────────────────────────────────

Write-Step "Computing resource group name"
$username = [Environment]::UserName.ToLower() -replace '[^a-z0-9]', ''
$env:TF_VAR_resource_group_name = "rg-dotnetagent-localdev-$username"
Write-Ok "Resource group: $($env:TF_VAR_resource_group_name)"

# ── Terraform Init ──────────────────────────────────────────────────────────

Write-Step "Initializing Terraform"
terraform -chdir="$TerraformDir" init -input=false
if ($LASTEXITCODE -ne 0) {
    Write-Fail "Terraform init failed (exit code $LASTEXITCODE)"
    exit 1
}
Write-Ok "Terraform initialized"

# ── Terraform Apply ─────────────────────────────────────────────────────────

Write-Step "Applying Terraform (this may take a few minutes)"
terraform -chdir="$TerraformDir" apply -auto-approve -input=false
if ($LASTEXITCODE -ne 0) {
    Write-Fail "Terraform apply failed (exit code $LASTEXITCODE)"
    exit 1
}
Write-Ok "Terraform apply complete"

# ── Retrieve Outputs ────────────────────────────────────────────────────────

Write-Step "Retrieving Terraform outputs"
$outputsJson = terraform -chdir="$TerraformDir" output -json
if ($LASTEXITCODE -ne 0) {
    Write-Fail "Failed to retrieve Terraform outputs"
    exit 1
}
$outputs = $outputsJson | ConvertFrom-Json
$foundryEndpoint       = $outputs.foundry_endpoint.value
$chatDeploymentName    = $outputs.chat_deployment_name.value
$embeddingDeploymentName = $outputs.embedding_deployment_name.value
$tenantId              = $outputs.tenant_id.value
$bffClientId           = $outputs.bff_client_id.value
$customerMapJson       = $outputs.customer_map_json.value
$testUserUpns          = $outputs.test_user_upns.value
Write-Ok "Foundry endpoint: $foundryEndpoint"
Write-Ok "Chat deployment: $chatDeploymentName"
Write-Ok "Embedding deployment: $embeddingDeploymentName"
Write-Ok "Tenant ID: $tenantId"
Write-Ok "BFF SPA client ID: $bffClientId"

# Pull sensitive password output via terraform's targeted -raw mode so we
# never write it to disk. We only print to the operator at the end.
$testUserPasswordsJson = terraform -chdir="$TerraformDir" output -json test_user_passwords
$testUserPasswords = $testUserPasswordsJson | ConvertFrom-Json

# ── Generate appsettings.Local.json from Templates ──────────────────────────

Write-Step "Generating appsettings.Local.json files"

foreach ($component in $TemplateComponents) {
    $templatePath = Join-Path $RepoRoot "src/$component/appsettings.Local.json.template"
    $outputPath   = Join-Path $RepoRoot "src/$component/appsettings.Local.json"

    if (-not (Test-Path $templatePath)) {
        Write-Warn "Template not found: $templatePath — skipping"
        continue
    }

    # Use String.Replace (literal) so that JSON values containing regex
    # metacharacters (`$`, `\`) substitute cleanly into the template.
    $content = (Get-Content $templatePath -Raw).
        Replace('{{FOUNDRY_ENDPOINT}}',          $foundryEndpoint).
        Replace('{{CHAT_DEPLOYMENT_NAME}}',      $chatDeploymentName).
        Replace('{{EMBEDDING_DEPLOYMENT_NAME}}', $embeddingDeploymentName).
        Replace('{{TENANT_ID}}',                 $tenantId).
        Replace('{{BFF_CLIENT_ID}}',             $bffClientId).
        Replace('{{CUSTOMER_MAP_JSON}}',         $customerMapJson)

    Set-Content -Path $outputPath -Value $content -NoNewline
    Write-Ok "Generated src/$component/appsettings.Local.json"
}

foreach ($component in $StaticComponents) {
    $templatePath = Join-Path $RepoRoot "src/$component/appsettings.Local.json.template"
    $outputPath   = Join-Path $RepoRoot "src/$component/appsettings.Local.json"

    if (-not (Test-Path $templatePath)) {
        Write-Warn "Template not found: $templatePath — skipping"
        continue
    }

    Copy-Item $templatePath $outputPath -Force
    Write-Ok "Generated src/$component/appsettings.Local.json"
}

# ── Summary ─────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host "  Local Dev Setup Complete" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Sign in to the Blazor UI as one of these test users" -ForegroundColor White
Write-Host "  (saved in your Entra tenant; passwords printed once):" -ForegroundColor White
foreach ($key in ($testUserUpns.PSObject.Properties.Name | Sort-Object)) {
    $upn = $testUserUpns.$key
    $password = $testUserPasswords.$key
    Write-Host ("    {0,-7} {1,-50} {2}" -f $key, $upn, $password)
}
Write-Host ""
Write-Host "  Port Map:" -ForegroundColor White
foreach ($entry in $PortMap) {
    Write-Host "    $($entry.Port)  $($entry.Component)"
}
Write-Host ""
Write-Host "  Run all services:" -ForegroundColor White
Write-Host "    dotnet run --project src/AppHost" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Or run individual components:" -ForegroundColor White
Write-Host "    dotnet run --project src/crm-api --environment Local" -ForegroundColor Yellow
Write-Host ""
Write-Host "  To tear down:" -ForegroundColor White
Write-Host "    .\infra\setup-local.ps1 -Cleanup" -ForegroundColor Yellow
Write-Host ""
