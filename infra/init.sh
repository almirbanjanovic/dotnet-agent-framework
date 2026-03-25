#!/usr/bin/env bash
set -euo pipefail

# ═══════════════════════════════════════════════════════════════════════════════
# .NET Agent Framework — Lab 0 Bootstrap
#
# Usage:  ./init.sh
# ═══════════════════════════════════════════════════════════════════════════════

SUBSCRIPTION_ID=""
GITHUB_ENV="dev"
LOCATION="eastus2"
BASE_NAME="dotnetagent"
APP_CLIENT_ID=""

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TERRAFORM_DIR="$SCRIPT_DIR/terraform"

# ── Helpers ──────────────────────────────────────────────────────────────────
C='\033[36m' G='\033[32m' R='\033[31m' D='\033[90m' Y='\033[33m' W='\033[0m'

# Wrap az CLI to strip Windows \r\n from stdout (WSL may call Windows az.cmd)
az() {
    local out rc
    out=$(command az "$@") && rc=$? || rc=$?
    [[ -n "$out" ]] && printf '%s\n' "$out" | tr -d '\r'
    return $rc
}

banner() {
    echo -e ""
    echo -e "  ${C}╔═══════════════════════════════════════════════════════╗${W}"
    echo -e "  ${C}║                                                       ║${W}"
    echo -e "  ${C}║   .NET Agent Framework — Lab 0 Bootstrap              ║${W}"
    echo -e "  ${C}║                                                       ║${W}"
    echo -e "  ${C}║   This script sets up everything you need:            ║${W}"
    echo -e "  ${C}║     1. Authenticate (Azure + GitHub)                  ║${W}"
    echo -e "  ${C}║     2. Entra app + OIDC + RBAC                        ║${W}"
    echo -e "  ${C}║     3. GitHub environment, secrets, variables         ║${W}"
    echo -e "  ${C}║     4. Azure backend (RG, storage, container)         ║${W}"
    echo -e "  ${C}║     5. Config files (<env>.tfvars, backend.hcl)       ║${W}"
    echo -e "  ${C}║                                                       ║${W}"
    echo -e "  ${C}╚═══════════════════════════════════════════════════════╝${W}"
    echo -e ""
}

phase() {
    echo -e ""
    echo -e "  ${D}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${W}"
    echo -e "  ${C}Phase $1 — $2${W}"
    echo -e "  ${D}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${W}"
}

step()  { echo -e "  → $1"; }
done_() { echo -e "    ${G}✓ $1${W}"; }
skip_() { echo -e "    ${D}· $1${W}"; }

phase_summary() {
    local num="$1"; local next_desc="$2"; shift 2
    
    # Build content lines and find max width
    local header=" Phase $num complete "
    local lines=()
    while [[ $# -gt 0 ]]; do
        lines+=("  $1: $2")
        shift 2
    done
    
    local max_len=${#header}
    for line in "${lines[@]}"; do
        (( ${#line} > max_len )) && max_len=${#line}
    done
    local box_width=$((max_len + 2))
    
    local top_fill=$(printf '─%.0s' $(seq 1 $((box_width - ${#header}))))
    local bot_fill=$(printf '─%.0s' $(seq 1 $box_width))
    
    echo ""
    echo -e "    ${G}┌${header}${top_fill}┐${W}"
    for line in "${lines[@]}"; do
        local pad_len=$((box_width - ${#line}))
        local pad=$(printf ' %.0s' $(seq 1 $pad_len))
        echo -e "    ${G}│${W}${line}${pad}${G}│${W}"
    done
    echo -e "    ${G}└${bot_fill}┘${W}"
    if [[ -n "$next_desc" ]]; then
        echo -e "    ${D}Next:${W} ${C}${next_desc}${W}"
    fi
    read -p "    Continue? (Y/n) " response
    if [[ "$response" == "n" || "$response" == "N" ]]; then
        echo -e "    ${Y}Stopped by user.${W}"
        exit 0
    fi
}

# ── Derived names ────────────────────────────────────────────────────────────
RESOURCE_GROUP="rg-${BASE_NAME}-${GITHUB_ENV}-${LOCATION}"
STORAGE_ACCOUNT="st$(echo "$RESOURCE_GROUP" | sed 's/^rg-//' | tr -cd 'a-z0-9')"
STORAGE_ACCOUNT="${STORAGE_ACCOUNT:0:24}"

# ═══════════════════════════════════════════════════════════════════════════════
# Prerequisites
# ═══════════════════════════════════════════════════════════════════════════════

banner

step "Checking & installing prerequisites"

install_if_missing() {
    local cmd="$1" name="$2"
    if command -v "$cmd" >/dev/null 2>&1; then
        done_ "$name ($cmd)"
        return
    fi

    echo -e "    ${Y}Installing $name...${W}"

    if [[ "$(uname)" == "Darwin" ]]; then
        if ! command -v brew >/dev/null 2>&1; then
            echo "Homebrew not found. Install from https://brew.sh then re-run."; exit 1
        fi
        case "$cmd" in
            az)        brew install azure-cli ;;
            gh)        brew install gh ;;
            terraform) brew install hashicorp/tap/terraform ;;
            dotnet)    brew install --cask dotnet-sdk ;;
        esac
    elif command -v apt-get >/dev/null 2>&1; then
        case "$cmd" in
            az)        curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash ;;
            gh)
                (type -p wget >/dev/null || sudo apt-get install wget -y)
                sudo mkdir -p -m 755 /etc/apt/keyrings
                wget -qO- https://cli.github.com/packages/githubcli-archive-keyring.gpg | sudo tee /etc/apt/keyrings/githubcli-archive-keyring.gpg > /dev/null
                echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" | sudo tee /etc/apt/sources.list.d/github-cli.list > /dev/null
                sudo apt-get update && sudo apt-get install gh -y ;;
            terraform)
                wget -qO- https://apt.releases.hashicorp.com/gpg | sudo gpg --dearmor -o /usr/share/keyrings/hashicorp-archive-keyring.gpg
                echo "deb [signed-by=/usr/share/keyrings/hashicorp-archive-keyring.gpg] https://apt.releases.hashicorp.com $(lsb_release -cs) main" | sudo tee /etc/apt/sources.list.d/hashicorp.list
                sudo apt-get update && sudo apt-get install terraform -y ;;
            dotnet)
                wget https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh
                chmod +x /tmp/dotnet-install.sh && /tmp/dotnet-install.sh --channel 9.0
                export PATH="$PATH:$HOME/.dotnet" ;;
        esac
    else
        echo "$name is not installed and no supported package manager found. Install manually."; exit 1
    fi

    if command -v "$cmd" >/dev/null 2>&1; then
        done_ "$name installed"
    else
        echo "Failed to install $name. Install manually and re-run."; exit 1
    fi
}

install_if_missing az        "Azure CLI"
install_if_missing terraform "Terraform"
install_if_missing dotnet    ".NET SDK"

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 1 — Authenticate
# ═══════════════════════════════════════════════════════════════════════════════

phase 1 "Authenticate"

# ── Azure ────────────────────────────────────────────────────────────────────
step "Signing in to Azure"

echo -e "    ${D}A browser tab will open \u2014 select the correct account.${W}"
# Disable interactive subscription picker and WAM broker
az config set core.login_experience_v2=off 2>/dev/null
az config set core.enable_broker_on_windows=false 2>/dev/null
# Request Graph scope upfront to avoid stale-token errors (TokenCreatedWithOutdatedPolicies)
# during Entra operations in Phase 2.
az login --scope https://graph.microsoft.com/.default >/dev/null

if [[ -z "$SUBSCRIPTION_ID" ]]; then
    current_id=$(az account show --query id -o tsv)
    mapfile -t sub_names < <(az account list --query "[].name" -o tsv)
    mapfile -t sub_ids   < <(az account list --query "[].id" -o tsv)
    sub_count=${#sub_names[@]}
    echo ""
    echo -e "    ${D}Available subscriptions:${W}"
    echo ""
    for (( i=0; i<sub_count; i++ )); do
        if [[ "${sub_ids[$i]}" == "$current_id" ]]; then
            marker="*" color="$C"
        else
            marker=" " color="$W"
        fi
        echo -e "    ${color}${marker} $((i+1)). ${sub_names[$i]}${W}"
        echo -e "         ${D}${sub_ids[$i]}${W}"
    done
    echo ""
    echo -e "    ${D}* = current default${W}"
    echo ""
    read -p "    Select subscription [1-${sub_count}, or press Enter for current]: " pick
    if [[ -n "$pick" && "$pick" =~ ^[0-9]+$ ]]; then
        idx=$((pick - 1))
        if (( idx >= 0 && idx < sub_count )); then
            SUBSCRIPTION_ID="${sub_ids[$idx]}"
            az account set --subscription "$SUBSCRIPTION_ID"
        else
            echo "Invalid selection: $pick"; exit 1
        fi
    else
        SUBSCRIPTION_ID="$current_id"
    fi
else
    az account set --subscription "$SUBSCRIPTION_ID"
fi

SUB_NAME=$(az account show --query name -o tsv)
TENANT_ID=$(az account show --query tenantId -o tsv)
done_ "Azure: $SUB_NAME ($SUBSCRIPTION_ID)"

# ── Deployment mode ──────────────────────────────────────────────────────────
echo ""
echo -e "    ${C}┌─────────────────────────────────────────────────────────┐${W}"
echo -e "    ${C}│  How would you like to deploy?                          │${W}"
echo -e "    ${C}│                                                         │${W}"
echo -e "    ${C}│    1. Full setup — Azure + GitHub Actions CI/CD         │${W}"
echo -e "    ${C}│       (Entra OIDC app, GitHub secrets, variables)       │${W}"
echo -e "    ${C}│                                                         │${W}"
echo -e "    ${C}│    2. Local only — Azure backend only                   │${W}"
echo -e "    ${C}│       (Deploy with ./deploy.ps1 or ./deploy.sh)         │${W}"
echo -e "    ${C}└─────────────────────────────────────────────────────────┘${W}"
echo ""
read -p "    Select [1-2, or press Enter for full setup]: " mode_choice
LOCAL_ONLY=false
[[ "$mode_choice" == "2" ]] && LOCAL_ONLY=true

if $LOCAL_ONLY; then
    done_ "Mode: Local only (GitHub CI/CD will be skipped)"
else
    done_ "Mode: Full setup (Azure + GitHub CI/CD)"
    install_if_missing gh "GitHub CLI"
fi

if ! $LOCAL_ONLY; then

if gh auth status &>/dev/null; then
    skip_ "Already logged in to GitHub"
else
    if grep -qi microsoft /proc/version 2>/dev/null; then
        echo -e "    ${D}WSL detected — using device code flow (no browser auto-open).${W}"
        echo -e "    ${D}You'll get a code to enter at https://github.com/login/device${W}"
        echo ""
        gh auth login --hostname github.com --git-protocol https
    else
        echo -e "    ${D}A browser tab will open for authentication.${W}"
        echo ""
        gh auth login --hostname github.com --git-protocol https --web
    fi
fi

GITHUB_REPO=$(gh repo view --json nameWithOwner -q ".nameWithOwner" 2>/dev/null || true)

if [[ -n "$GITHUB_REPO" ]]; then
    echo -e "    Detected repo: ${C}${GITHUB_REPO}${W}"
    read -p "    Use this repo? (Y/n) " confirm_repo
    if [[ "$confirm_repo" == "n" || "$confirm_repo" == "N" ]]; then
        read -p "    Enter repo (owner/name): " GITHUB_REPO
    fi
else
    echo -e "    ${Y}No GitHub repo detected.${W}"
    gh_user=$(gh api user --jq .login 2>/dev/null || echo "unknown")
    default_name=$(basename "$(git rev-parse --show-toplevel 2>/dev/null)" 2>/dev/null || echo "dotnet-agent-framework")

    read -p "    (C)reate new repo '$gh_user/$default_name' or (E)nter existing? [C/e] " action
    if [[ "$action" == "e" || "$action" == "E" ]]; then
        read -p "    Enter repo (owner/name): " GITHUB_REPO
    else
        echo "    Creating repo $gh_user/$default_name..."
        gh repo create "$default_name" --private --source . --push
        GITHUB_REPO="$gh_user/$default_name"
        done_ "Created $GITHUB_REPO"
    fi
fi

if [[ -z "$GITHUB_REPO" ]]; then echo "No GitHub repository configured."; exit 1; fi
REPO_NAME="${GITHUB_REPO##*/}"
done_ "GitHub: $GITHUB_REPO"

fi # end if ! $LOCAL_ONLY

# ── Environment ──────────────────────────────────────────────────────────────
step "Select environment"
echo ""
echo -e "    ${D}Choose an environment for this deployment:${W}"
echo -e "    ${D}  1. dev       (development — default)${W}"
echo -e "    ${D}  2. staging   (pre-production)${W}"
echo -e "    ${D}  3. prod      (production)${W}"
echo -e "    ${D}  4. custom    (enter your own name)${W}"
echo ""
read -p "    Select [1-4, or press Enter for dev]: " env_choice

case "$env_choice" in
    2) GITHUB_ENV="staging" ;;
    3) GITHUB_ENV="prod" ;;
    4) read -p "    Enter environment name: " GITHUB_ENV ;;
    *) ;; # keep the default or what was passed via --env
esac
done_ "Environment: $GITHUB_ENV"

# ── Region ───────────────────────────────────────────────────────────────────
step "Select Azure region"

VALID_REGIONS=(
    eastus eastus2 centralus northcentralus southcentralus
    westus westus2 westus3
    canadacentral canadaeast
    brazilsouth
    westeurope northeurope
    francecentral
    germanywestcentral
    norwayeast
    swedencentral
    switzerlandnorth
    uksouth
    italynorth
    spaincentral
    eastasia southeastasia
    australiaeast
    japaneast
    koreacentral
    centralindia
    uaenorth
    qatarcentral
    southafricanorth
)

REGION_COUNT=${#VALID_REGIONS[@]}

# Helper: print a row of numbered regions (up to 3 per line)
print_region_row() {
    local line=""
    for i in "$@"; do
        line+=$(printf "%2d) %-16s" "$i" "${VALID_REGIONS[$((i-1))]}")
    done
    echo -e "       ${D}${line}${W}"
}

echo ""
echo -e "    ${D}Azure Region (grouped by data residency zone)${W}"
echo -e "    ${D}──────────────────────────────────────────────${W}"

echo -e "    ${D}United States${W}"
print_region_row 1 2 3
print_region_row 4 5 6
print_region_row 7 8
echo -e "    ${D}Canada${W}"
print_region_row 9 10
echo -e "    ${D}Brazil${W}"
print_region_row 11
echo -e "    ${D}Europe${W}"
print_region_row 12 13
echo -e "    ${D}France${W}"
print_region_row 14
echo -e "    ${D}Germany${W}"
print_region_row 15
echo -e "    ${D}Norway${W}"
print_region_row 16
echo -e "    ${D}Sweden${W}"
print_region_row 17
echo -e "    ${D}Switzerland${W}"
print_region_row 18
echo -e "    ${D}United Kingdom${W}"
print_region_row 19
echo -e "    ${D}Italy${W}"
print_region_row 20
echo -e "    ${D}Spain${W}"
print_region_row 21
echo -e "    ${D}Asia Pacific${W}"
print_region_row 22 23
echo -e "    ${D}Australia${W}"
print_region_row 24
echo -e "    ${D}Japan${W}"
print_region_row 25
echo -e "    ${D}Korea${W}"
print_region_row 26
echo -e "    ${D}India${W}"
print_region_row 27
echo -e "    ${D}UAE${W}"
print_region_row 28
echo -e "    ${D}Qatar${W}"
print_region_row 29
echo -e "    ${D}South Africa${W}"
print_region_row 30
echo ""

DEFAULT_INDEX=2   # eastus2

while true; do
    read -p "    Select region [$DEFAULT_INDEX]: " region_input
    if [[ -z "$region_input" ]]; then
        LOCATION="${VALID_REGIONS[$((DEFAULT_INDEX-1))]}"
        break
    fi
    region_input=$(echo "$region_input" | xargs)
    # Check if input is a number
    if [[ "$region_input" =~ ^[0-9]+$ ]]; then
        if (( region_input >= 1 && region_input <= REGION_COUNT )); then
            LOCATION="${VALID_REGIONS[$((region_input-1))]}"
            break
        fi
        echo -e "    ${R}✗ Invalid selection '$region_input'. Enter a number 1-${REGION_COUNT} or a region name.${W}"
        continue
    fi
    # Backward compatibility: accept region name
    region_input=$(echo "$region_input" | tr '[:upper:]' '[:lower:]')
    for r in "${VALID_REGIONS[@]}"; do
        if [[ "$r" == "$region_input" ]]; then
            LOCATION="$region_input"
            break 2
        fi
    done
    echo -e "    ${R}✗ Invalid region '$region_input'. Enter a number 1-${REGION_COUNT} or a region name.${W}"
done
done_ "Region: $LOCATION"

# ── Recalculate derived names with final values ──────────────────────────────
RESOURCE_GROUP="rg-${BASE_NAME}-${GITHUB_ENV}-${LOCATION}"
STORAGE_ACCOUNT="st$(echo "$RESOURCE_GROUP" | sed 's/^rg-//' | tr -cd 'a-z0-9')"
STORAGE_ACCOUNT="${STORAGE_ACCOUNT:0:24}"

if $LOCAL_ONLY; then
    phase_summary 1 \
        "Phase 4 — Create Azure resource group, storage account, blob container" \
        "Subscription"   "$SUB_NAME ($SUBSCRIPTION_ID)" \
        "Tenant"         "$TENANT_ID" \
        "Location"       "$LOCATION" \
        "Base name"      "$BASE_NAME" \
        "Environment"    "$GITHUB_ENV" \
        "Resource group" "$RESOURCE_GROUP" \
        "Deploy mode"    "Local only"
else
    phase_summary 1 \
        "Phase 2 — Create Entra app registration, service principal, and OIDC federated credential" \
        "Subscription"   "$SUB_NAME ($SUBSCRIPTION_ID)" \
        "Tenant"         "$TENANT_ID" \
        "GitHub repo"    "$GITHUB_REPO" \
        "Location"       "$LOCATION" \
        "Base name"      "$BASE_NAME" \
        "Environment"    "$GITHUB_ENV" \
        "Resource group" "$RESOURCE_GROUP" \
        "Deploy mode"    "Full (Azure + GitHub)"
fi

if ! $LOCAL_ONLY; then

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 2 — Entra app registration + OIDC + RBAC
# ═══════════════════════════════════════════════════════════════════════════════

phase 2 "Entra app registration + OIDC + RBAC"

APP_NAME="github-actions-${REPO_NAME}"

step "Creating app registration"
existing=$(az ad app list --display-name "$APP_NAME" --query "[0].appId" -o tsv 2>/dev/null || true)
if [[ -n "$existing" ]]; then
    APP_CLIENT_ID="$existing"
    skip_ "App '$APP_NAME' already exists: $APP_CLIENT_ID"
else
    APP_CLIENT_ID=$(az ad app create --display-name "$APP_NAME" --query appId -o tsv 2>/dev/null || true)
    if [[ -z "$APP_CLIENT_ID" ]]; then
        # Retry once — CAE challenge can occur if Entra policies changed since login.
        # Must clear cached tokens and do a full interactive login to satisfy the challenge.
        echo -e "    ${Y}\u26a0 Entra operation failed. Clearing cached tokens and re-authenticating...${W}"
        az account clear 2>/dev/null
        az login --scope https://graph.microsoft.com/.default --tenant "$TENANT_ID"
        az account set --subscription "$SUBSCRIPTION_ID"
        APP_CLIENT_ID=$(az ad app create --display-name "$APP_NAME" --query appId -o tsv)
        if [[ -z "$APP_CLIENT_ID" ]]; then
            echo "Failed to create app registration. Check your permissions."; exit 1
        fi
    fi
    done_ "Created app: $APP_CLIENT_ID"
fi

sp_exists=$(az ad sp show --id "$APP_CLIENT_ID" --query id -o tsv 2>/dev/null || true)
if [[ -n "$sp_exists" ]]; then
    skip_ "Service principal already exists"
else
    az ad sp create --id "$APP_CLIENT_ID" >/dev/null
    done_ "Created service principal"
fi

step "Adding OIDC federated credential"
CRED_NAME="${REPO_NAME}-${GITHUB_ENV}"
existing_cred=$(az ad app federated-credential list --id "$APP_CLIENT_ID" --query "[?name=='$CRED_NAME'].name" -o tsv 2>/dev/null || true)
if [[ -n "$existing_cred" ]]; then
    skip_ "Federated credential '$CRED_NAME' already exists"
else
    az ad app federated-credential create --id "$APP_CLIENT_ID" --parameters '{
        "name": "'"$CRED_NAME"'",
        "issuer": "https://token.actions.githubusercontent.com",
        "subject": "repo:'"$GITHUB_REPO"':environment:'"$GITHUB_ENV"'",
        "audiences": ["api://AzureADTokenExchange"],
        "description": "GitHub Actions OIDC for '"$REPO_NAME"' ('"$GITHUB_ENV"')"
    }' >/dev/null
    done_ "Federated credential for repo:${GITHUB_REPO}:environment:${GITHUB_ENV}"
fi

phase_summary 2 \
    "Phase 3 — Create GitHub environment, set repository secrets and environment variables" \
    "App registration" "$APP_NAME ($APP_CLIENT_ID)" \
    "OIDC subject"     "repo:${GITHUB_REPO}:environment:${GITHUB_ENV}" \
    "Credential name"  "$CRED_NAME" \
    "RBAC"             "Contributor on $RESOURCE_GROUP (granted in Phase 4)"

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 3 — GitHub environment, secrets, variables
# ═══════════════════════════════════════════════════════════════════════════════

phase 3 "GitHub environment, secrets, variables"

# ── Create environment first ─────────────────────────────────────────────────
step "Creating GitHub environment '$GITHUB_ENV'"
gh api --method PUT "repos/$GITHUB_REPO/environments/$GITHUB_ENV" >/dev/null 2>&1 || true
done_ "Environment '$GITHUB_ENV' ready"

step "Setting repository secrets"
gh secret set AZURE_CLIENT_ID --repo "$GITHUB_REPO" --body "$APP_CLIENT_ID"
done_ "AZURE_CLIENT_ID"
gh secret set AZURE_TENANT_ID --repo "$GITHUB_REPO" --body "$TENANT_ID"
done_ "AZURE_TENANT_ID"
gh secret set AZURE_SUBSCRIPTION_ID --repo "$GITHUB_REPO" --body "$SUBSCRIPTION_ID"
done_ "AZURE_SUBSCRIPTION_ID"

step "Setting environment variables ($GITHUB_ENV)"

declare -A ENV_VARS=(
    [RESOURCE_GROUP]="$RESOURCE_GROUP"
    [LOCATION]="$LOCATION"
    [STORAGE_ACCOUNT]="$STORAGE_ACCOUNT"
    [STORAGE_ACCOUNT_SKU]="Standard_LRS"
    [STORAGE_ACCOUNT_ENCRYPTION_SERVICES]="blob"
    [STORAGE_ACCOUNT_MIN_TLS_VERSION]="TLS1_2"
    [STORAGE_ACCOUNT_PUBLIC_NETWORK_ACCESS]="Enabled"
    [TERRAFORM_STATE_CONTAINER]="tfstate"
    [TERRAFORM_STATE_BLOB]="${GITHUB_ENV}.tfstate"
    [TERRAFORM_WORKING_DIRECTORY]="infra/terraform"
    [TAGS]="{}"
    [ENVIRONMENT]="$GITHUB_ENV"
    [BASE_NAME]="$BASE_NAME"
    [COGNITIVE_ACCOUNT_KIND]="AIServices"
    [OAI_SKU_NAME]="S0"
    [OAI_DEPLOYMENT_SKU_NAME]="GlobalStandard"
    [OAI_DEPLOYMENT_MODEL_FORMAT]="OpenAI"
    [OAI_DEPLOYMENT_MODEL_NAME]="gpt-4.1"
    [OAI_DEPLOYMENT_MODEL_VERSION]="2025-04-14"
    [OAI_VERSION_UPGRADE_OPTION]="NoAutoUpgrade"
    [CREATE_EMBEDDING_DEPLOYMENT]="true"
    [EMBEDDING_MODEL_NAME]="text-embedding-ada-002"
    [EMBEDDING_MODEL_VERSION]="2"
    [EMBEDDING_SKU_NAME]="Standard"
    [EMBEDDING_CAPACITY]="10"
    [COSMOS_AGENTS_DATABASE_NAME]="agents"
    [COSMOS_AGENT_STATE_CONTAINER_NAME]="workshop_agent_state_store"
    [COSMOS_CRM_DATABASE_NAME]="contoso-crm"
    [SEARCH_SKU]="standard"
    [SEARCH_INDEX_NAME]="knowledge-documents"
    [CREATE_ACR]="true"
    [ACR_SKU]="Premium"
    [ACR_NAME]="$(echo "acr${BASE_NAME}${GITHUB_ENV}${LOCATION}" | tr -d '-')"
    [AKS_KUBERNETES_VERSION]="1.34"
    [AKS_SYSTEM_NODE_VM_SIZE]="Standard_D2s_v3"
    [AKS_WORKLOAD_NODE_VM_SIZE]="Standard_D2s_v3"
    [AKS_AUTO_SCALING_ENABLED]="true"
    [AKS_OS_DISK_SIZE_GB]="64"
    [AKS_LOG_RETENTION_DAYS]="30"
)

count=0
for key in "${!ENV_VARS[@]}"; do
    val="${ENV_VARS[$key]}"
    if [[ -z "$val" ]]; then val=" "; fi
    gh variable set "$key" --repo "$GITHUB_REPO" --env "$GITHUB_ENV" --body "$val"
    count=$((count + 1))
done
done_ "Set $count environment variables in '$GITHUB_ENV'"

phase_summary 3 \
    "Phase 4 — Create Azure resource group, storage account, blob container, and assign RBAC" \
    "Secrets"       "AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_SUBSCRIPTION_ID" \
    "Environment"   "$GITHUB_ENV" \
    "Env variables" "$count"

fi # end if ! $LOCAL_ONLY — phases 2 & 3

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 4 — Azure backend resources
# ═══════════════════════════════════════════════════════════════════════════════

phase 4 "Azure backend resources"

step "Creating resource group"
if ! az group show --name "$RESOURCE_GROUP" &>/dev/null; then
    az group create --name "$RESOURCE_GROUP" --location "$LOCATION" >/dev/null
    done_ "Created $RESOURCE_GROUP in $LOCATION"
else
    skip_ "$RESOURCE_GROUP already exists"
fi

step "Creating storage account for Terraform state"
WAIT_TIME=30
if ! az storage account show --resource-group "$RESOURCE_GROUP" --name "$STORAGE_ACCOUNT" &>/dev/null; then
    az storage account create \
        --resource-group "$RESOURCE_GROUP" --name "$STORAGE_ACCOUNT" \
        --sku "Standard_LRS" --encryption-services blob \
        --min-tls-version "TLS1_2" --location "$LOCATION" \
        --default-action Deny >/dev/null
    echo "    Waiting ${WAIT_TIME}s for storage account..."
    sleep $WAIT_TIME
    done_ "Created $STORAGE_ACCOUNT"
else
    skip_ "$STORAGE_ACCOUNT already exists"
    az storage account update --name "$STORAGE_ACCOUNT" --resource-group "$RESOURCE_GROUP" --default-action Deny -o none 2>/dev/null
    sleep $WAIT_TIME
fi

CONTAINER_NAME="tfstate"
if ! az storage container show --name "$CONTAINER_NAME" --account-name "$STORAGE_ACCOUNT" --auth-mode login &>/dev/null; then
    az storage container create --name "$CONTAINER_NAME" --account-name "$STORAGE_ACCOUNT" --auth-mode login >/dev/null
    done_ "Created container $CONTAINER_NAME"
else
    skip_ "Container $CONTAINER_NAME already exists"
fi

step "Locking down state storage"
az storage account update --name "$STORAGE_ACCOUNT" --resource-group "$RESOURCE_GROUP" --default-action Deny -o none 2>/dev/null
done_ "Default network action set to Deny"

# ── RBAC: Contributor scoped to resource group (least privilege) ─────────────
if ! $LOCAL_ONLY && [[ -n "$APP_CLIENT_ID" ]]; then
    step "Granting Contributor role on resource group"
    rg_scope="/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP"
    role_exists=$(az role assignment list --assignee "$APP_CLIENT_ID" --role "Contributor" --scope "$rg_scope" --query "[0].id" -o tsv 2>/dev/null || true)
    if [[ -n "$role_exists" ]]; then
        skip_ "Contributor role already assigned on $RESOURCE_GROUP"
    else
        az role assignment create --assignee "$APP_CLIENT_ID" --role "Contributor" --scope "$rg_scope" >/dev/null
        done_ "Contributor granted on $RESOURCE_GROUP"
    fi

    # ── Graph API: Application.ReadWrite.All for Agent Identity (Entra Agent ID) ──
    step "Granting Application.ReadWrite.All (Graph API) for Agent Identity"
    APP_RW_ALL_ID="1bfefb4e-e0b5-418b-a88f-73c46d2cc8e9"
    EXISTING_PERM=$(az ad app permission list --id "$APP_CLIENT_ID" \
        --query "[?resourceAppId=='00000003-0000-0000-c000-000000000000'].resourceAccess[?id=='$APP_RW_ALL_ID'].id" \
        -o tsv 2>/dev/null || true)
    if [[ -n "$EXISTING_PERM" ]]; then
        skip_ "Application.ReadWrite.All already granted"
    else
        az ad app permission add --id "$APP_CLIENT_ID" \
            --api "00000003-0000-0000-c000-000000000000" \
            --api-permissions "${APP_RW_ALL_ID}=Role" 2>/dev/null || true
        done_ "Application.ReadWrite.All added"
    fi
    az ad app permission admin-consent --id "$APP_CLIENT_ID" 2>/dev/null || true
    done_ "Admin consent applied"
fi

if $LOCAL_ONLY; then
    RBAC_STATUS="Skipped (local-only mode)"
else
    RBAC_STATUS="Contributor on $RESOURCE_GROUP"
fi

# ── Deployer identity (from ARM token — no Graph API required) ───────────────
step "Determining deployer identity"
DEPLOYER_OID=""
TOKEN=$(az account get-access-token --query accessToken -o tsv 2>/dev/null)
if [[ -n "$TOKEN" ]]; then
    PAYLOAD=$(echo "$TOKEN" | cut -d. -f2)
    # Fix base64 padding
    MOD=$((${#PAYLOAD} % 4))
    if [[ $MOD -eq 2 ]]; then PAYLOAD="${PAYLOAD}=="; elif [[ $MOD -eq 3 ]]; then PAYLOAD="${PAYLOAD}="; fi
    DEPLOYER_OID=$(echo "$PAYLOAD" | base64 -d 2>/dev/null | python3 -c "import sys,json; print(json.load(sys.stdin).get('oid',''))" 2>/dev/null || \
                   echo "$PAYLOAD" | base64 -d 2>/dev/null | jq -r '.oid // empty' 2>/dev/null || true)
fi

if [[ -z "$DEPLOYER_OID" ]]; then
    echo ""
    echo -e "    ${R}\u2717 Could not determine deployer identity from access token.${W}"
    echo -e "    ${R}RBAC roles are required for Terraform to manage state and secrets.${W}"
    echo ""
    echo -e "    ${Y}Troubleshooting:${W}"
    echo -e "    ${Y}  1. Ensure you're logged in: az login${W}"
    echo -e "    ${Y}  2. Check your account: az account show${W}"
    echo ""
    exit 1
fi

done_ "Deployer identity: $DEPLOYER_OID"

# ── RBAC: Storage Blob Data Contributor for deployer (Terraform state) ────────────────
step "Granting Storage Blob Data Contributor to deployer on state storage"
STORAGE_RBAC_STATUS="Manual assignment required"
ST_SCOPE=$(az storage account show --name "$STORAGE_ACCOUNT" --resource-group "$RESOURCE_GROUP" --query id -o tsv 2>/dev/null || true)
if [[ -n "$ST_SCOPE" ]]; then
    BLOB_ROLE="Storage Blob Data Contributor"
    EXISTS=$(az role assignment list --assignee "$DEPLOYER_OID" --role "$BLOB_ROLE" --scope "$ST_SCOPE" --query "[0].id" -o tsv 2>/dev/null || true)
    if [[ -n "$EXISTS" ]]; then
        skip_ "$BLOB_ROLE already assigned"
    else
        az role assignment create --assignee-object-id "$DEPLOYER_OID" --assignee-principal-type User --role "$BLOB_ROLE" --scope "$ST_SCOPE" >/dev/null
        done_ "$BLOB_ROLE granted on $STORAGE_ACCOUNT"
    fi
    STORAGE_RBAC_STATUS="Blob Data Contributor on $STORAGE_ACCOUNT"
else
    echo -e "    ${Y}\u26a0 Could not find storage account $STORAGE_ACCOUNT \u2014 assign Storage Blob Data Contributor manually${W}"
fi

# ── RBAC: Key Vault data-plane roles for deployer (current user) ───────────────
step "Granting Key Vault data-plane roles to deployer"
KV_RBAC_STATUS="Secrets Officer + Certificates Officer"
RG_SCOPE="/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP"
for ROLE in "Key Vault Secrets Officer" "Key Vault Certificates Officer"; do
    EXISTS=$(az role assignment list --assignee "$DEPLOYER_OID" --role "$ROLE" --scope "$RG_SCOPE" --query "[0].id" -o tsv 2>/dev/null || true)
    if [[ -n "$EXISTS" ]]; then
        skip_ "$ROLE already assigned"
    else
        az role assignment create --assignee-object-id "$DEPLOYER_OID" --assignee-principal-type User --role "$ROLE" --scope "$RG_SCOPE" >/dev/null
        done_ "$ROLE granted on $RESOURCE_GROUP"
    fi
done

phase_summary 4 \
    "Phase 5 — Generate ${GITHUB_ENV}.tfvars and backend.hcl configuration files" \
    "Resource group"   "$RESOURCE_GROUP" \
    "Storage account"  "$STORAGE_ACCOUNT" \
    "Container"        "$CONTAINER_NAME" \
    "RBAC"             "$RBAC_STATUS" \
    "Storage RBAC"     "$STORAGE_RBAC_STATUS" \
    "KV RBAC"          "$KV_RBAC_STATUS" \
    "Public access"    "Disabled"

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 5 — Generate config files
# ═══════════════════════════════════════════════════════════════════════════════

phase 5 "Generate configuration files"

cat > "$TERRAFORM_DIR/backend.hcl" <<EOF
resource_group_name  = "$RESOURCE_GROUP"
storage_account_name = "$STORAGE_ACCOUNT"
container_name       = "$CONTAINER_NAME"
key                  = "$GITHUB_ENV.tfstate"
use_azuread_auth     = true
EOF
done_ "backend.hcl"

cat > "$TERRAFORM_DIR/${GITHUB_ENV}.tfvars" <<EOF
tags                = {}
resource_group_name = "$RESOURCE_GROUP"
environment         = "$GITHUB_ENV"
base_name           = "$BASE_NAME"
location            = "$LOCATION"

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
acr_name          = "$(echo "acr${BASE_NAME}${GITHUB_ENV}${LOCATION}" | tr -d '-')"

# AKS
aks_kubernetes_version       = "1.34"
aks_system_node_vm_size      = "Standard_D2s_v3"
aks_workload_node_vm_size    = "Standard_D2s_v3"
aks_auto_scaling_enabled     = true
aks_os_disk_size_gb          = 64
aks_log_retention_days       = 30
EOF
done_ "${GITHUB_ENV}.tfvars"

# ── Write deploy.env (consumed by deploy scripts) ───────────────────────────
DEPLOY_ENV_PATH="$SCRIPT_DIR/deploy.env"
TIMESTAMP=$(date '+%Y-%m-%d %H:%M:%S %z')
cat > "$DEPLOY_ENV_PATH" <<EOF
# Generated by init script — do not edit manually
# Re-run init to regenerate
# Generated: $TIMESTAMP
ENVIRONMENT=$GITHUB_ENV
LOCATION=$LOCATION
BASE_NAME=$BASE_NAME
RESOURCE_GROUP=$RESOURCE_GROUP
EOF
done_ "deploy.env"

# ═══════════════════════════════════════════════════════════════════════════════

if $LOCAL_ONLY; then
    echo ""
    echo -e "  ${G}╔═══════════════════════════════════════════════════════════════╗${W}"
    echo -e "  ${G}║  Bootstrap Complete!                                          ║${W}"
    echo -e "  ${G}╠═══════════════════════════════════════════════════════════════╣${W}"
    echo -e "  ${G}║${W}  Subscription:     $SUB_NAME"
    echo -e "  ${G}║${W}  Location:         $LOCATION"
    echo -e "  ${G}║${W}  Resource group:   $RESOURCE_GROUP"
    echo -e "  ${G}║${W}  Storage account:  $STORAGE_ACCOUNT"
    echo -e "  ${G}╠═══════════════════════════════════════════════════════════════╣${W}"
    echo -e "  ${G}║${W}"
    echo -e "  ${Y}║  ⚠  LOCAL-ONLY MODE${W}"
    echo -e "  ${Y}║     GitHub CI/CD was NOT configured${W}"
    echo -e "  ${G}║${W}"
    echo -e "  ${G}║${W}  Deployments must be run manually:"
    echo -e "  ${G}║${W}    cd infra && ./deploy.ps1  (or ./deploy.sh)"
    echo -e "  ${G}║${W}"
    echo -e "  ${G}║${W}  GitHub Actions workflows will NOT work until"
    echo -e "  ${G}║${W}  you re-run this script and select option 1."
    echo -e "  ${G}║${W}"
    echo -e "  ${G}╚═══════════════════════════════════════════════════════════════╝${W}"
    echo ""
else
    echo ""
    echo -e "  ${G}╔═══════════════════════════════════════════════════════╗${W}"
    echo -e "  ${G}║  Bootstrap Complete!                                  ║${W}"
    echo -e "  ${G}╠═══════════════════════════════════════════════════════╣${W}"
    echo -e "  ${G}║${W}  Subscription:     $SUB_NAME"
    echo -e "  ${G}║${W}  Location:         $LOCATION"
    echo -e "  ${G}║${W}  Resource group:   $RESOURCE_GROUP"
    echo -e "  ${G}║${W}  Storage account:  $STORAGE_ACCOUNT"
    echo -e "  ${G}║${W}  App registration: $APP_CLIENT_ID"
    echo -e "  ${G}║${W}  GitHub repo:      $GITHUB_REPO"
    echo -e "  ${G}║${W}  GitHub env:       $GITHUB_ENV"
    echo -e "  ${G}║${W}  Secrets:          3"
    echo -e "  ${G}║${W}  Env variables:    $count"
    echo -e "  ${G}║${W}"
    echo -e "  ${G}║  Next: proceed to Lab 1 (terraform apply)             ║${W}"
    echo -e "  ${G}╚═══════════════════════════════════════════════════════╝${W}"
    echo ""
fi
