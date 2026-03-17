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
C='\033[36m' G='\033[32m' D='\033[90m' Y='\033[33m' W='\033[0m'

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
    echo -e "  ${C}║     5. Config files (terraform.tfvars, backend.hcl)   ║${W}"
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
install_if_missing gh        "GitHub CLI"
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

# ── GitHub ───────────────────────────────────────────────────────────────────
step "Signing in to GitHub"

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

# ── Recalculate derived names with final values ──────────────────────────────
RESOURCE_GROUP="rg-${BASE_NAME}-${GITHUB_ENV}-${LOCATION}"
STORAGE_ACCOUNT="st$(echo "$RESOURCE_GROUP" | sed 's/^rg-//' | tr -cd 'a-z0-9')"
STORAGE_ACCOUNT="${STORAGE_ACCOUNT:0:24}"

phase_summary 1 \
    "Phase 2 — Create Entra app registration, service principal, and OIDC federated credential" \
    "Subscription"   "$SUB_NAME ($SUBSCRIPTION_ID)" \
    "Tenant"         "$TENANT_ID" \
    "GitHub repo"    "$GITHUB_REPO" \
    "Location"       "$LOCATION" \
    "Base name"      "$BASE_NAME" \
    "Environment"    "$GITHUB_ENV" \
    "Resource group" "$RESOURCE_GROUP"

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
        # Retry once — CAE challenge can occur if Entra policies changed since login
        echo -e "    ${Y}\u26a0 Entra operation failed. Re-authenticating with Graph scope...${W}"
        az login --scope https://graph.microsoft.com/.default --tenant "$TENANT_ID" --only-show-errors --output none
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
    [SQL_DATABASE_NAME]="contoso-outdoors"
    [SQL_ADMIN_LOGIN]="sqladmin"
    [SEARCH_SKU]="basic"
    [SEARCH_INDEX_NAME]="knowledge-documents"
    [CREATE_ACR]="true"
    [ACR_SKU]="Premium"
    [ACR_NAME]="$(echo "acr${BASE_NAME}${GITHUB_ENV}${LOCATION}" | tr -d '-')"
    [AKS_KUBERNETES_VERSION]="1.34"
    [AKS_SYSTEM_NODE_VM_SIZE]="Standard_D4s_v5"
    [AKS_WORKLOAD_NODE_VM_SIZE]="Standard_D4s_v5"
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
        --public-network-access Enabled >/dev/null
    echo "    Waiting ${WAIT_TIME}s for storage account..."
    sleep $WAIT_TIME
    done_ "Created $STORAGE_ACCOUNT"
else
    skip_ "$STORAGE_ACCOUNT already exists"
    az storage account update --name "$STORAGE_ACCOUNT" --resource-group "$RESOURCE_GROUP" --public-network-access Enabled >/dev/null
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
az storage account update --name "$STORAGE_ACCOUNT" --resource-group "$RESOURCE_GROUP" --public-network-access Disabled >/dev/null
done_ "Public access disabled"

# ── RBAC: Contributor scoped to resource group (least privilege) ─────────────
if [[ -n "$APP_CLIENT_ID" ]]; then
    step "Granting Contributor role on resource group"
    rg_scope="/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP"
    role_exists=$(az role assignment list --assignee "$APP_CLIENT_ID" --role "Contributor" --scope "$rg_scope" --query "[0].id" -o tsv 2>/dev/null || true)
    if [[ -n "$role_exists" ]]; then
        skip_ "Contributor role already assigned on $RESOURCE_GROUP"
    else
        az role assignment create --assignee "$APP_CLIENT_ID" --role "Contributor" --scope "$rg_scope" >/dev/null
        done_ "Contributor granted on $RESOURCE_GROUP"
    fi
fi

phase_summary 4 \
    "Phase 5 — Generate terraform.tfvars and backend.hcl configuration files" \
    "Resource group"  "$RESOURCE_GROUP" \
    "Storage account" "$STORAGE_ACCOUNT" \
    "Container"       "$CONTAINER_NAME" \
    "RBAC"            "Contributor on $RESOURCE_GROUP" \
    "Public access"   "Disabled"

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

# Azure SQL Database (CRM operational data)
sql_database_name = "contoso-outdoors"
sql_admin_login   = "sqladmin"

# AI Search
search_sku        = "basic"
search_index_name = "knowledge-documents"

# ACR
create_acr        = true
acr_sku           = "Premium"
acr_name          = "$(echo "acr${BASE_NAME}${GITHUB_ENV}${LOCATION}" | tr -d '-')"

# AKS
aks_kubernetes_version       = "1.34"
aks_system_node_vm_size      = "Standard_D4s_v5"
aks_workload_node_vm_size    = "Standard_D4s_v5"
aks_auto_scaling_enabled     = true
aks_os_disk_size_gb          = 64
aks_log_retention_days       = 30
EOF
done_ "${GITHUB_ENV}.tfvars"

# ═══════════════════════════════════════════════════════════════════════════════

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
