<#
.SYNOPSIS
    Builds, pushes, and deploys the CRM API to AKS.

.PARAMETER Environment
    Target environment: dev, staging, or prod. Default: dev

.PARAMETER AcrName
    Azure Container Registry name (e.g., contosoacr). Required.

.PARAMETER ImageTag
    Docker image tag. Defaults to git short SHA.

.PARAMETER SkipBuild
    Skip Docker build and push — deploy Helm chart only.
#>
[CmdletBinding()]
param(
    [ValidateSet("dev", "staging", "prod")]
    [string]$Environment = "dev",

    [Parameter(Mandatory = $true)]
    [string]$AcrName,

    [string]$ImageTag = "",

    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ServiceName = "crm-api"
$Namespace = "contoso"
$ScriptDir = $PSScriptRoot
$RepoRoot = (Resolve-Path "$ScriptDir/../..").Path

# Default image tag to git short SHA
if (-not $ImageTag) {
    $ImageTag = (git -C $RepoRoot rev-parse --short HEAD 2>$null)
    if (-not $ImageTag) {
        $ImageTag = "latest"
    }
}

$ImageRepository = "$AcrName.azurecr.io/$ServiceName"
$FullImageRef = "${ImageRepository}:${ImageTag}"

Write-Host "╔══════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  CRM API Deploy                                      ║" -ForegroundColor Cyan
Write-Host "║  Environment: $Environment                           ║" -ForegroundColor Cyan
Write-Host "║  Image:       $FullImageRef                          ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════╝" -ForegroundColor Cyan

# ── Docker Build & Push ─────────────────────────────────────────────────────
if (-not $SkipBuild) {
    Write-Host "`n→ Building Docker image..." -ForegroundColor Yellow

    docker build `
        -t $FullImageRef `
        -f "$ScriptDir/Dockerfile" `
        --build-arg BUILD_VERSION=$ImageTag `
        --build-arg BUILD_DATE=$(Get-Date -Format "o") `
        --build-arg VCS_REF=$(git -C $RepoRoot rev-parse HEAD 2>$null) `
        $RepoRoot

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Docker build failed."
        exit 1
    }

    Write-Host "→ Pushing image to ACR..." -ForegroundColor Yellow
    az acr login --name $AcrName
    docker push $FullImageRef

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Docker push failed."
        exit 1
    }

    Write-Host "✓ Image pushed: $FullImageRef" -ForegroundColor Green
}

# ── Helm Deploy ─────────────────────────────────────────────────────────────
Write-Host "`n→ Deploying Helm chart..." -ForegroundColor Yellow

$ChartDir = "$ScriptDir/chart"

helm upgrade --install $ServiceName $ChartDir `
    --namespace $Namespace `
    --create-namespace `
    --set image.repository=$ImageRepository `
    --set image.tag=$ImageTag `
    --set config.ASPNETCORE_ENVIRONMENT=$Environment `
    --wait `
    --timeout 5m

if ($LASTEXITCODE -ne 0) {
    Write-Error "Helm deploy failed."
    exit 1
}

Write-Host "`n✓ $ServiceName deployed successfully to $Namespace namespace." -ForegroundColor Green
Write-Host "  kubectl get pods -n $Namespace -l app.kubernetes.io/name=$ServiceName" -ForegroundColor DarkGray
