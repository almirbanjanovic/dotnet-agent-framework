#!/usr/bin/env bash
set -euo pipefail

# ═══════════════════════════════════════════════════════════════════════════════
# .NET Agent Framework — Lab 0 Bootstrap
#
# Usage:
#   ./init.sh
#   ./init.sh --location centralus --base-name myproject
#   ./init.sh --skip-entra --app-client-id "12345678-..."
# ═══════════════════════════════════════════════════════════════════════════════

SUBSCRIPTION_ID=""
GITHUB_ENV="dev"
LOCATION="eastus2"
BASE_NAME="dotnetagent"
SKIP_ENTRA=false
APP_CLIENT_ID=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --subscription)    SUBSCRIPTION_ID="$2"; shift 2 ;;
        --env)             GITHUB_ENV="$2"; shift 2 ;;
        --location)        LOCATION="$2"; shift 2 ;;
        --base-name)       BASE_NAME="$2"; shift 2 ;;
        --skip-entra)      SKIP_ENTRA=true; shift ;;
        --app-client-id)   APP_CLIENT_ID="$2"; shift 2 ;;
        *)                 echo "Unknown option: $1"; exit 1 ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TERRAFORM_DIR="$SCRIPT_DIR/terraform"

# ── Helpers ──────────────────────────────────────────────────────────────────
C='\033[36m' G='\033[32m' D='\033[90m' Y='\033[33m' W='\033[0m'

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
    local num="$1"; shift
    echo ""
    echo -e "    ${G}┌ Phase $num complete ─────────────────────────────────┐${W}"
    while [[ $# -gt 0 ]]; do
        echo -e "    ${G}│${W}  $1: $2"
        shift 2
    done
    echo -e "    ${G}└─────────────────────────────────────────────────────┘${W}"
    read -p "    Continue to next phase? (Y/n) " response
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
step "Signing in to Azure (device code)"
az login --use-device-code >/dev/null

if [[ -z "$SUBSCRIPTION_ID" ]]; then
    echo ""
    az account list --query "[].{Name:name, Id:id, IsDefault:isDefault}" -o table
    echo ""
    current_sub=$(az account show --query name -o tsv)
    current_id=$(az account show --query id -o tsv)
    echo -e "    Current subscription: ${C}${current_sub} (${current_id})${W}"
    read -p "    Use this subscription? (Y/n) " change_it
    if [[ "$change_it" == "n" || "$change_it" == "N" ]]; then
        read -p "    Enter subscription ID: " SUBSCRIPTION_ID
        az account set --subscription "$SUBSCRIPTION_ID"
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
echo ""
echo -e "    ${D}Logging in via browser (HTTPS). A browser window will open.${W}"
echo ""
gh auth login --hostname github.com --git-protocol https --web

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

phase_summary 1 \
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

if [[ "$SKIP_ENTRA" == "true" ]]; then
    if [[ -z "$APP_CLIENT_ID" ]]; then echo "--skip-entra requires --app-client-id"; exit 1; fi
    skip_ "Skipping Entra setup (using existing app: $APP_CLIENT_ID)"
else
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
            echo -e "    ${Y}App registration failed. Re-authenticating...${W}"
            az login --use-device-code >/dev/null
            APP_CLIENT_ID=$(az ad app create --display-name "$APP_NAME" --query appId -o tsv)
        fi
        if [[ -z "$APP_CLIENT_ID" ]]; then
            echo "Failed to create app registration. Check your permissions."; exit 1
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

    step "Granting Contributor role on subscription"
    role_exists=$(az role assignment list --assignee "$APP_CLIENT_ID" --role "Contributor" --scope "/subscriptions/$SUBSCRIPTION_ID" --query "[0].id" -o tsv 2>/dev/null || true)
    if [[ -n "$role_exists" ]]; then
        skip_ "Contributor role already assigned"
    else
        az role assignment create --assignee "$APP_CLIENT_ID" --role "Contributor" --scope "/subscriptions/$SUBSCRIPTION_ID" >/dev/null
        done_ "Contributor granted on $SUBSCRIPTION_ID"
    fi

    phase_summary 2 \
        "App registration" "$APP_NAME ($APP_CLIENT_ID)" \
        "OIDC subject"     "repo:${GITHUB_REPO}:environment:${GITHUB_ENV}" \
        "Credential name"  "$CRED_NAME" \
        "RBAC"             "Contributor on subscription"
fi

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 3 — GitHub environment, secrets, variables
# ═══════════════════════════════════════════════════════════════════════════════

phase 3 "GitHub environment, secrets, variables"

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
    [COSMOS_PROJECT_NAME]="$BASE_NAME"
    [COSMOS_AGENTS_DATABASE_NAME]="agents"
    [COSMOS_AGENT_STATE_CONTAINER_NAME]="workshop_agent_state_store"
    [SQL_DATABASE_NAME]="contoso-outdoors"
    [SQL_ADMIN_LOGIN]="sqladmin"
    [STORAGE_PROJECT_NAME]="$BASE_NAME"
    [SEARCH_SKU]="basic"
    [SEARCH_INDEX_NAME]="knowledge-documents"
    [ACR_PROJECT_NAME]="$BASE_NAME"
    [CREATE_ACR]="true"
    [ACR_SKU]="Premium"
    [EXISTING_ACR_NAME]=""
    [AKS_KUBERNETES_VERSION]=""
    [AKS_NODE_VM_SIZE]="Standard_D4s_v5"
    [AKS_NODE_COUNT]="2"
    [AKS_AUTO_SCALING_ENABLED]="true"
    [AKS_NODE_MIN_COUNT]="1"
    [AKS_NODE_MAX_COUNT]="5"
    [AKS_OS_DISK_SIZE_GB]="64"
    [AKS_LOG_RETENTION_DAYS]="30"
)

count=0
for key in "${!ENV_VARS[@]}"; do
    gh variable set "$key" --repo "$GITHUB_REPO" --env "$GITHUB_ENV" --body "${ENV_VARS[$key]}"
    count=$((count + 1))
done
done_ "Set $count environment variables in '$GITHUB_ENV'"

phase_summary 3 \
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

phase_summary 4 \
    "Resource group"  "$RESOURCE_GROUP" \
    "Storage account" "$STORAGE_ACCOUNT" \
    "Container"       "$CONTAINER_NAME" \
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
EOF
done_ "backend.hcl"

cat > "$TERRAFORM_DIR/terraform.tfvars" <<EOF
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
cosmos_project_name               = "$BASE_NAME"
cosmos_agents_database_name       = "agents"
cosmos_agent_state_container_name = "workshop_agent_state_store"

# Azure SQL Database (CRM operational data)
sql_database_name = "contoso-outdoors"
sql_admin_login   = "sqladmin"

# Storage
storage_project_name = "$BASE_NAME"

# AI Search
search_sku        = "basic"
search_index_name = "knowledge-documents"

# ACR
acr_project_name  = "$BASE_NAME"
create_acr        = true
acr_sku           = "Premium"
existing_acr_name = ""

# AKS
aks_kubernetes_version   = null
aks_node_vm_size         = "Standard_D4s_v5"
aks_node_count           = 2
aks_auto_scaling_enabled = true
aks_node_min_count       = 1
aks_node_max_count       = 5
aks_os_disk_size_gb      = 64
aks_log_retention_days   = 30
EOF
done_ "terraform.tfvars"

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
