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
$TerraformDir = Join-Path $RepoRoot "infra/terraform/local-dev"

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
    @{ Port = 5010; Component = "fraud-workflow" }
)

# Templates that need Foundry placeholder replacement
$TemplateComponents = @(
    "simple-agent",
    "knowledge-mcp",
    "crm-agent",
    "product-agent",
    "orchestrator-agent",
    "bff-api",
    "blazor-ui",
    "fraud-workflow"
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

# ── Backend Bootstrap (shared by Cleanup and main path) ────────────────────
#
# The local-dev stack stores Terraform state REMOTELY in Azure Blob Storage,
# co-located with the rest of the stack inside the working resource group
# `rg-dotnetagent-localdev`. Storage account, container, and the working RG
# itself are bootstrapped via the Azure CLI (out-of-band of Terraform), so
# `terraform destroy` never touches them — state survives:
#   - `setup-local -Cleanup` (which only runs `terraform destroy`)
#   - re-running `setup-local` end-to-end (idempotent create-if-absent)
# To wipe everything for real, run `az group delete --name rg-dotnetagent-localdev`.
#
# This function is idempotent and runs in BOTH cleanup and main flows.
function Initialize-Backend {
    param(
        [string]$WorkingResourceGroup,
        [string]$Location
    )

    # ── Working RG (out-of-band; Terraform reads it via a data source) ─────
    az group create `
        --name $WorkingResourceGroup `
        --location $Location `
        --tags "managed-by=setup-local" "purpose=local-development" `
        --output none
    if ($LASTEXITCODE -ne 0) { Write-Fail "Failed to create working RG $WorkingResourceGroup"; exit 1 }

    # ── State backend names (deterministic, subscription-scoped) ───────────
    $subId = az account show --query id -o tsv 2>$null
    if ([string]::IsNullOrWhiteSpace($subId)) {
        Write-Fail "Could not determine subscription ID"
        exit 1
    }
    # Subscription ID hashed → 8 hex chars for storage-account uniqueness.
    # Using a hash (not the raw subId prefix) avoids leaking subscription
    # identifiers into a globally-visible storage-account name.
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $hashBytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($subId))
    $sha.Dispose()
    $suffix = (-join ($hashBytes | ForEach-Object { $_.ToString("x2") })).Substring(0, 8)
    $storageAccount = "stdotnetagentldtf$suffix"  # 17 + 8 = 25 chars… trim to 24
    if ($storageAccount.Length -gt 24) { $storageAccount = $storageAccount.Substring(0, 24) }
    $containerName = "tfstate"
    $stateKey      = "local-dev.tfstate"

    Write-Step "Bootstrapping Terraform state backend"
    Write-Ok "Resource group:   $WorkingResourceGroup"
    Write-Ok "Storage account:  $storageAccount"
    Write-Ok "Container / key:  $containerName / $stateKey"

    # ── Storage account (idempotent) ───────────────────────────────────────
    $null = az storage account show --resource-group $WorkingResourceGroup --name $storageAccount 2>&1
    if ($LASTEXITCODE -ne 0) {
        az storage account create `
            --resource-group $WorkingResourceGroup `
            --name $storageAccount `
            --location $Location `
            --sku Standard_LRS `
            --kind StorageV2 `
            --min-tls-version TLS1_2 `
            --allow-blob-public-access false `
            --output none
        if ($LASTEXITCODE -ne 0) { Write-Fail "Failed to create storage account $storageAccount"; exit 1 }
        Write-Ok "Created storage account"
    } else {
        Write-Ok "Storage account already exists"
    }

    # ── RBAC: Storage Blob Data Contributor for the deployer ──────────────
    # Required for `use_azuread_auth = true` (no storage keys involved).
    # Capture stderr so a CAE token-refresh challenge or missing Graph
    # consent isn't silently swallowed (which used to surface only as a
    # generic "Could not determine deployer object ID" message).
    $deployerOidOutput = az ad signed-in-user show --query id -o tsv 2>&1
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($deployerOidOutput)) {
        Write-Fail "Could not determine deployer object ID. 'az ad signed-in-user show' said:"
        Write-Host "  $deployerOidOutput"
        Write-Host ""
        Write-Host "Hint: this is usually a stale Azure CLI token (Continuous Access"
        Write-Host "Evaluation challenge) or missing Microsoft Graph consent. Try:"
        Write-Host "  az login"
        Write-Host "  ./infra/setup-local.ps1"
        exit 1
    }
    $deployerOid = $deployerOidOutput.Trim()
    $stScope = az storage account show --name $storageAccount --resource-group $WorkingResourceGroup --query id -o tsv
    $existingRole = az role assignment list `
        --assignee $deployerOid `
        --scope $stScope `
        --role "Storage Blob Data Contributor" `
        --query "[0].id" -o tsv 2>$null
    if (-not $existingRole) {
        az role assignment create `
            --assignee-object-id $deployerOid `
            --assignee-principal-type User `
            --role "Storage Blob Data Contributor" `
            --scope $stScope --output none 2>$null
        Write-Ok "Granted Storage Blob Data Contributor — waiting 30s for RBAC propagation"
        Start-Sleep -Seconds 30
    } else {
        Write-Ok "Storage Blob Data Contributor already assigned"
    }

    # ── Container (with retry — RBAC propagation can lag) ─────────────────
    $containerReady = $false
    for ($attempt = 1; $attempt -le 6; $attempt++) {
        $null = az storage container show --name $containerName --account-name $storageAccount --auth-mode login 2>&1
        if ($LASTEXITCODE -eq 0) { $containerReady = $true; break }
        $null = az storage container create --name $containerName --account-name $storageAccount --auth-mode login 2>&1
        if ($LASTEXITCODE -eq 0) { $containerReady = $true; break }
        if ($attempt -lt 6) {
            Write-Host "    Waiting for RBAC (attempt $attempt/6) — retrying in 15s" -ForegroundColor DarkGray
            Start-Sleep -Seconds 15
        }
    }
    if (-not $containerReady) { Write-Fail "Failed to create $containerName container"; exit 1 }
    Write-Ok "Container ready: $containerName"

    # ── Generate backend.hcl (gitignored — see infra/.gitignore) ──────────
    $backendFile = Join-Path $TerraformDir "backend.hcl"
    Set-Content -Path $backendFile -Encoding UTF8 -Value @"
resource_group_name  = "$WorkingResourceGroup"
storage_account_name = "$storageAccount"
container_name       = "$containerName"
key                  = "$stateKey"
use_azuread_auth     = true
"@
    Write-Ok "Generated backend.hcl"

    # ── terraform init with the remote backend ────────────────────────────
    # `-reconfigure` discards any prior backend config (e.g. legacy local
    # state from before this script was migrated to remote state) WITHOUT
    # auto-migrating. If you have a populated local state file you want to
    # migrate, run once manually:
    #   terraform -chdir=infra/terraform/local-dev init -migrate-state -backend-config=backend.hcl
    Write-Step "Initializing Terraform with remote backend"
    terraform -chdir="$TerraformDir" init -reconfigure -backend-config="$backendFile" -input=false
    if ($LASTEXITCODE -ne 0) { Write-Fail "terraform init failed"; exit 1 }
    Write-Ok "Terraform initialized (remote state in $storageAccount/$containerName/$stateKey)"
}

# ── Compute working RG name (used by both cleanup and main) ────────────────
$WorkingRg       = "rg-dotnetagent-localdev"
$WorkingLocation = if ([string]::IsNullOrWhiteSpace($env:TF_VAR_location)) { "centralus" } else { $env:TF_VAR_location }

# ── Cleanup Mode ────────────────────────────────────────────────────────────

if ($Cleanup) {
    # Initialize backend first so `terraform destroy` talks to the remote
    # state. The working RG, storage account, and container are NOT destroyed.
    Initialize-Backend -WorkingResourceGroup $WorkingRg -Location $WorkingLocation

    Write-Step "Destroying Terraform resources"
    terraform -chdir="$TerraformDir" destroy -auto-approve -input=false
    if ($LASTEXITCODE -ne 0) {
        Write-Fail "Terraform destroy failed (exit code $LASTEXITCODE)"
        exit 1
    }
    Write-Ok "Terraform resources destroyed (state backend in $WorkingRg preserved)"

    Write-Step "Removing generated appsettings.Local.json files"
    $allComponents = $TemplateComponents + $StaticComponents
    foreach ($component in $allComponents) {
        $settingsFile = Join-Path $RepoRoot "src/$component/appsettings.Local.json"
        if (Test-Path $settingsFile) {
            Remove-Item $settingsFile -Force
            Write-Ok "Removed src/$component/appsettings.Local.json"
        }
    }

    $credentialsFile = Join-Path $RepoRoot "local-dev-credentials.txt"
    if (Test-Path $credentialsFile) {
        Remove-Item $credentialsFile -Force
        Write-Ok "Removed local-dev-credentials.txt"
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
$env:TF_VAR_resource_group_name = $WorkingRg
Write-Ok "Resource group: $($env:TF_VAR_resource_group_name)"

# ── Bootstrap Terraform state backend + init ────────────────────────────────
# Initialize-Backend also creates the working RG (idempotent) since the state
# storage account lives there — the Terraform stack reads the RG via a `data`
# source, so it's never in TF state and `terraform destroy` won't touch it.
Initialize-Backend -WorkingResourceGroup $WorkingRg -Location $WorkingLocation

# ── Purge soft-deleted resources ────────────────────────────────────────────
# Cognitive Services accounts and Key Vaults use soft-delete by default. If a
# prior `setup-local.ps1 -Cleanup` ran (or terraform destroy), the soft-deleted
# resources block re-creation with `FlagMustBeSetForRestore` (HTTP 409). Purge
# them so the next `terraform apply` can recreate cleanly.
Write-Step "Purging soft-deleted resources (if any)"

$softCog = az cognitiveservices account list-deleted `
    --query "[?contains(id, '$($env:TF_VAR_resource_group_name)')].name" -o tsv 2>$null
foreach ($acct in ($softCog -split "`n" | Where-Object { $_ })) {
    $acct = $acct.Trim()
    # Recover the deleted account's location from its full ID so we don't have
    # to hardcode a region (the same RG name can host accounts in different
    # regions across re-deploys).
    $acctLoc = az cognitiveservices account list-deleted `
        --query "[?name=='$acct'].location | [0]" -o tsv 2>$null
    if ([string]::IsNullOrWhiteSpace($acctLoc)) { continue }
    az cognitiveservices account purge --location $acctLoc `
        --resource-group $env:TF_VAR_resource_group_name --name $acct 2>$null | Out-Null
    Write-Ok "Purged Cognitive Services: $acct ($acctLoc)"
}

$softKv = az keyvault list-deleted `
    --query "[?properties.vaultId && contains(properties.vaultId, '$($env:TF_VAR_resource_group_name)')].name" -o tsv 2>$null
foreach ($kv in ($softKv -split "`n" | Where-Object { $_ })) {
    $kv = $kv.Trim()
    az keyvault purge --name $kv --no-wait 2>$null | Out-Null
    Write-Ok "Purged Key Vault: $kv"
}

if (-not $softCog -and -not $softKv) {
    Write-Ok "No soft-deleted resources to purge"
}

# ── Delete orphan Entra test users ──────────────────────────────────────────
#
# Terraform owns these test users. They live in tenant-wide Entra, so a
# `terraform destroy` followed by `terraform apply` (or any state loss) can
# leave the UPN in Entra without a matching state entry — the next apply
# would then 409 with "user already exists".
#
# To avoid that we delete ONLY genuine orphans: UPNs that exist in Entra
# but have no corresponding `azuread_user.test["<key>"]` entry in Terraform
# state. Users that are already managed by Terraform are left alone, so
# repeat runs of setup-local are a no-op for them — no recreate, no
# password rotation, no invalidated browser sessions.
Write-Step "Checking for orphan Entra test users"

$tenantDomain = az rest --method GET --url 'https://graph.microsoft.com/v1.0/domains' `
    --query "value[?isDefault].id | [0]" -o tsv 2>$null
if ([string]::IsNullOrWhiteSpace($tenantDomain)) {
    Write-Fail "Could not determine default tenant domain via Graph"
    exit 1
}
Write-Ok "Tenant default domain: $tenantDomain"

# Snapshot the list of test users currently in Terraform state so we can
# tell "managed by TF" (skip) from "true orphan" (delete). This is a single
# remote-state read; subsequent UPN lookups are local string matches.
$tfStateLines = terraform -chdir="$TerraformDir" state list 2>$null
$managedUserKeys = @{}
if ($LASTEXITCODE -eq 0 -and $tfStateLines) {
    foreach ($line in $tfStateLines) {
        if ($line -match 'azuread_user\.test\["([^"]+)"\]') {
            $managedUserKeys[$matches[1]] = $true
        }
    }
}

# Mirror of `var.test_users` defaults in
# infra/terraform/modules/entra/v1/variables.tf — keep in sync.
# The `-local` suffix matches `mail_nickname_suffix` passed from
# infra/terraform/local-dev/main.tf so we look up the SAME UPNs Terraform
# will create. The Full Azure Track uses no suffix, so its `emma.wilson@`
# users are not deleted by this script.
$TestUserNicknames = [ordered]@{
    emma  = "emma.wilson-local"
    james = "james.chen-local"
    sarah = "sarah.miller-local"
    david = "david.park-local"
    lisa  = "lisa.torres-local"
    mike  = "mike.johnson-local"
    anna  = "anna.roberts-local"
    tom   = "tom.garcia-local"
}

$deletedCount = 0
$skippedCount = 0
foreach ($key in $TestUserNicknames.Keys) {
    $upn = "$($TestUserNicknames[$key])@$tenantDomain"
    $oid = az ad user show --id $upn --query id -o tsv 2>$null
    if ([string]::IsNullOrWhiteSpace($oid)) { continue }

    if ($managedUserKeys.ContainsKey($key)) {
        # Already in TF state — leave it alone. terraform apply will be a no-op.
        $skippedCount++
        continue
    }

    az ad user delete --id $upn 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Fail "Failed to delete orphan user $upn (object id $($oid.Trim()))"
        exit 1
    }
    Write-Ok "Deleted orphan: $upn"
    $deletedCount++
}
if ($deletedCount -eq 0) {
    if ($skippedCount -gt 0) {
        Write-Ok "$skippedCount test user(s) already managed by Terraform — leaving as-is"
    } else {
        Write-Ok "No orphan test users found"
    }
}

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
$foundryProjectEndpoint = $outputs.foundry_project_endpoint.value
$chatDeploymentName    = $outputs.chat_deployment_name.value
$embeddingDeploymentName = $outputs.embedding_deployment_name.value
$tenantId              = $outputs.tenant_id.value
$bffClientId           = $outputs.bff_client_id.value
$customerMapJson       = $outputs.customer_map_json.value
$testUserUpns          = $outputs.test_user_upns.value
Write-Ok "Foundry project endpoint: $foundryProjectEndpoint"
Write-Ok "Chat deployment: $chatDeploymentName"
Write-Ok "Embedding deployment: $embeddingDeploymentName"
Write-Ok "Tenant ID: $tenantId"
Write-Ok "BFF SPA client ID: $bffClientId"

# Pull sensitive password output via terraform's targeted -raw mode so we
# never write it to disk. We only print to the operator at the end.
$testUserPasswordsJson = terraform -chdir="$TerraformDir" output -json test_user_passwords
$testUserPasswords = $testUserPasswordsJson | ConvertFrom-Json

# ── Write test-user credentials to a gitignored file ───────────────────────
#
# Passwords are stable across `setup-local` runs — the `random_pet` /
# `random_integer` resources backing them stay in Terraform state, and we
# only delete genuine orphans (UPNs not in TF state) before applying.
# A password only changes after a `setup-local -Cleanup` (or a manual
# `terraform destroy`) followed by a fresh setup. Lab students need to
# copy individual passwords back into the Blazor sign-in dialog as they
# exercise different customer scenarios; printing them to the terminal
# isn't practical, so we write them to a per-clone gitignored file at the
# repo root. The file is rewritten in full on every apply so it always
# reflects the live passwords.
Write-Step "Writing test-user credentials"

$credentialsPath = Join-Path $RepoRoot "local-dev-credentials.txt"

$credLines = @(
    "# Local-dev test-user credentials"
    "# Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')"
    "# Tenant:    $tenantId"
    "# WARNING:   gitignored — do not commit. Passwords persist across setup-local runs;"
    "#            they only change after a -Cleanup followed by a fresh setup."
    ""
    ("{0,-7}  {1,-50}  {2}" -f "key", "upn", "password")
    ("{0,-7}  {1,-50}  {2}" -f "---", "---", "---")
)
foreach ($key in ($testUserUpns.PSObject.Properties.Name | Sort-Object)) {
    $upn = $testUserUpns.$key
    $password = $testUserPasswords.$key
    $credLines += ("{0,-7}  {1,-50}  {2}" -f $key, $upn, $password)
}
Set-Content -Path $credentialsPath -Value $credLines -Encoding UTF8
$credentialsRelative = (Resolve-Path -Relative $credentialsPath).TrimStart('.','/','\')
Write-Ok "Wrote $credentialsRelative"

# ── Generate appsettings.Local.json from Templates ──────────────────────────

Write-Step "Generating appsettings.Local.json files"

# Helper: blazor-ui is a WASM SPA; configuration is loaded from
# wwwroot/appsettings.Local.json (served over HTTP), not the project root.
# Every other component reads from the project root at process startup.
function Get-AppsettingsOutputPath {
    param([string]$Component)
    if ($Component -eq 'blazor-ui') {
        return Join-Path $RepoRoot "src/$Component/wwwroot/appsettings.Local.json"
    }
    return Join-Path $RepoRoot "src/$Component/appsettings.Local.json"
}

foreach ($component in $TemplateComponents) {
    $templatePath = Join-Path $RepoRoot "src/$component/appsettings.Local.json.template"
    $outputPath   = Get-AppsettingsOutputPath -Component $component

    if (-not (Test-Path $templatePath)) {
        Write-Warn "Template not found: $templatePath — skipping"
        continue
    }

    # Use String.Replace (literal) so that JSON values containing regex
    # metacharacters (`$`, `\`) substitute cleanly into the template.
    $content = (Get-Content $templatePath -Raw).
        Replace('{{FOUNDRY_PROJECT_ENDPOINT}}', $foundryProjectEndpoint).
        Replace('{{CHAT_DEPLOYMENT_NAME}}',      $chatDeploymentName).
        Replace('{{EMBEDDING_DEPLOYMENT_NAME}}', $embeddingDeploymentName).
        Replace('{{TENANT_ID}}',                 $tenantId).
        Replace('{{BFF_CLIENT_ID}}',             $bffClientId).
        Replace('{{CUSTOMER_MAP_JSON}}',         $customerMapJson)

    Set-Content -Path $outputPath -Value $content -NoNewline
    $relativePath = (Resolve-Path -Relative $outputPath).TrimStart('.','/','\')
    Write-Ok "Generated $relativePath"
}

foreach ($component in $StaticComponents) {
    $templatePath = Join-Path $RepoRoot "src/$component/appsettings.Local.json.template"
    $outputPath   = Get-AppsettingsOutputPath -Component $component

    if (-not (Test-Path $templatePath)) {
        Write-Warn "Template not found: $templatePath — skipping"
        continue
    }

    Copy-Item $templatePath $outputPath -Force
    $relativePath = (Resolve-Path -Relative $outputPath).TrimStart('.','/','\')
    Write-Ok "Generated $relativePath"
}

# ── Summary ─────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host "  Local Dev Setup Complete" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Sign-in credentials for the 8 test users have been written to:" -ForegroundColor White
Write-Host "    $credentialsRelative" -ForegroundColor Yellow
Write-Host "  (gitignored — passwords persist across runs; -Cleanup rotates them)" -ForegroundColor DarkGray
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
