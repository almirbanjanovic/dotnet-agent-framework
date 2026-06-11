# Terraform tests for local-dev module using PowerShell
$ErrorActionPreference = "Stop"

# Get the script directory regardless of how it's called
$scriptPath = $PSCommandPath
if (-not $scriptPath) {
    $scriptPath = $MyInvocation.MyCommandPath
}
$scriptDir = Split-Path -Parent $scriptPath
$tfDir = Join-Path $scriptDir "..\..\infra\terraform\local-dev"

Push-Location $tfDir

try {
    Write-Host "================================" -ForegroundColor Cyan
    Write-Host "Terraform Tests: local-dev" -ForegroundColor Cyan
    Write-Host "================================" -ForegroundColor Cyan
    Write-Host ""

    # Test 1: terraform init succeeds
    Write-Host "Test 1: terraform init succeeds"
    terraform init -backend=false -input=false 2>&1 | Out-Null
    Write-Host "  PASS" -ForegroundColor Green
    
    # Test 2: terraform validate succeeds
    Write-Host "Test 2: terraform validate succeeds"
    terraform validate 2>&1 | Out-Null
    Write-Host "  PASS" -ForegroundColor Green
    
    # Test 3: All expected outputs defined
    Write-Host "Test 3: All expected outputs defined"
    # Note: foundry_api_key was intentionally removed when the local-dev module
    # switched to RBAC-only auth (DefaultAzureCredential). See setup-local + the
    # NoApiKeyTests fitness function.
    $expectedOutputs = @("foundry_project_endpoint", "chat_deployment_name", "embedding_deployment_name", "tenant_id", "bff_client_id", "customer_map_json")
    $outputsFile = Get-Content outputs.tf -Raw
    
    foreach ($output in $expectedOutputs) {
        if ($outputsFile -notmatch "output `"$output`"") {
            Write-Host "  FAIL: missing output $output" -ForegroundColor Red
            exit 1
        }
    }
    Write-Host "  PASS" -ForegroundColor Green
    
    # Test 4: Module variables have sensible defaults
    Write-Host "Test 4: Module variables have sensible defaults"
    $variablesFile = Get-Content variables.tf -Raw
    $requiredVars = @("location", "base_name", "environment", "chat_model_name", "chat_model_version", 
                      "embedding_model_name", "embedding_model_version", "resource_group_name")
    
    foreach ($var in $requiredVars) {
        $pattern = "variable `"$var`""
        if ($variablesFile -notmatch $pattern) {
            Write-Host "  FAIL: variable $var not defined" -ForegroundColor Red
            exit 1
        }

        $segmentPattern = "(?s)variable\s+`"$var`"\s*\{.*?\}"
        $segmentMatch = [regex]::Match($variablesFile, $segmentPattern)
        if (-not $segmentMatch.Success) {
            Write-Host "  FAIL: could not parse variable block for $var" -ForegroundColor Red
            exit 1
        }

        if ($segmentMatch.Value -notmatch "default\s*=") {
            Write-Host "  FAIL: variable $var missing default" -ForegroundColor Red
            exit 1
        }
    }
    Write-Host "  PASS" -ForegroundColor Green
    
    Write-Host ""
    Write-Host "================================" -ForegroundColor Cyan
    Write-Host "All 4 tests passed!" -ForegroundColor Cyan
    Write-Host "================================" -ForegroundColor Cyan
}
finally {
    Pop-Location
}
