#!/usr/bin/env bash
set -euo pipefail

# ═══════════════════════════════════════════════════════════════════════════════
# One-time bootstrap: creates Azure backend resources, Entra app registration
# with OIDC for GitHub Actions, and configures GitHub repository secrets + variables.
#
# Usage:
#   ./init.sh
#   ./init.sh --subscription "12345678-..." --repo "myorg/myrepo"
#   ./init.sh --skip-entra --app-client-id "12345678-..."
# ═══════════════════════════════════════════════════════════════════════════════

# ── Defaults ─────────────────────────────────────────────────────────────────
SUBSCRIPTION_ID=""
GITHUB_REPO=""
GITHUB_ENV="dev"
APP_NAME=""
SKIP_ENTRA=false
APP_CLIENT_ID=""

# ── Parse arguments ──────────────────────────────────────────────────────────
while [[ $# -gt 0 ]]; do
    case "$1" in
        --subscription)    SUBSCRIPTION_ID="$2"; shift 2 ;;
        --repo)            GITHUB_REPO="$2"; shift 2 ;;
        --env)             GITHUB_ENV="$2"; shift 2 ;;
        --app-name)        APP_NAME="$2"; shift 2 ;;
        --skip-entra)      SKIP_ENTRA=true; shift ;;
        --app-client-id)   APP_CLIENT_ID="$2"; shift 2 ;;
        *)                 echo "Unknown option: $1"; exit 1 ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TERRAFORM_DIR="$SCRIPT_DIR/terraform"

# ── Helpers ──────────────────────────────────────────────────────────────────
C='\033[36m' G='\033[32m' D='\033[90m' W='\033[0m' # cyan, green, dim, reset

banner() {
    echo -e ""
    echo -e "  ${C}╔═══════════════════════════════════════════════════════╗${W}"
    echo -e "  ${C}║                                                       ║${W}"
    echo -e "  ${C}║   .NET Agent Framework — Lab 0 Bootstrap              ║${W}"
    echo -e "  ${C}║                                                       ║${W}"
    echo -e "  ${C}║   This script sets up everything you need:            ║${W}"
    echo -e "  ${C}║     1. Config files (terraform.tfvars, backend.hcl)   ║${W}"
    echo -e "  ${C}║     2. Azure backend (RG, storage, container)         ║${W}"
    echo -e "  ${C}║     3. Entra app + OIDC federation                    ║${W}"
    echo -e "  ${C}║     4. GitHub secrets + environment variables         ║${W}"
    echo -e "  ${C}║     5. Lock down state storage                        ║${W}"
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

parse_hcl_value() {
    local file="$1" key="$2"
    grep -E "^\s*${key}\s*=" "$file" | head -1 | sed 's/.*=\s*"\(.*\)".*/\1/'
}

# ── Verify & install prerequisites ──────────────────────────────────────────
banner
step "Checking & installing prerequisites"

install_if_missing() {
    local cmd="$1" name="$2"
    if command -v "$cmd" >/dev/null 2>&1; then
        done_ "$name ($cmd)"
        return
    fi

    echo -e "    \033[33mInstalling $name...\033[0m"

    if [[ "$(uname)" == "Darwin" ]]; then
        # macOS — use Homebrew
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
        # Debian/Ubuntu
        case "$cmd" in
            az)
                curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash ;;
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

# ── Authenticate ───────────────────────────────────────────────────────────────
step "Authenticating"

echo "    Signing in to Azure CLI..."
az login >/dev/null
acct=$(az account show --query "{name:name, id:id}" -o tsv)
done_ "Azure: $acct"

echo "    Signing in to GitHub CLI..."
if ! gh auth status &>/dev/null; then
    gh auth login
fi
gh_user=$(gh api user --jq .login 2>/dev/null || echo "unknown")
done_ "GitHub: $gh_user"

# PHASE 1 — Generate config files
# ═══════════════════════════════════════════════════════════════════════════════

phase 1 "Generate configuration files"

TFVARS_FILE="$TERRAFORM_DIR/terraform.tfvars"

if [[ ! -f "$TFVARS_FILE" ]]; then
    cat > "$TFVARS_FILE" <<'TFVARS'
tags                = {}
resource_group_name = "rg-dotnetagent-dev-centralus"

environment = "dev"
base_name   = "dotnetagent"
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
# Cosmos DB (1 account: agents session state)
# ---------------------------------------------------------------
cosmos_project_name               = "dotnetagent"
cosmos_agents_database_name       = "agents"
cosmos_agent_state_container_name = "workshop_agent_state_store"

# ---------------------------------------------------------------
# Azure SQL Database (CRM operational data)
# ---------------------------------------------------------------
sql_database_name = "contoso-outdoors"
sql_admin_login   = "sqladmin"

# ---------------------------------------------------------------
# Storage
# ---------------------------------------------------------------
storage_project_name = "dotnetagent"

# ---------------------------------------------------------------
# AI Search
# ---------------------------------------------------------------
search_sku        = "basic"
search_index_name = "knowledge-documents"

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
TFVARS
    done_ "Generated $TFVARS_FILE with default values"
    echo "         (Edit this file to customize resource names, regions, or SKUs)"
else
    skip_ "Using existing $TFVARS_FILE"
fi

# ── Read values from terraform.tfvars ────────────────────────────────────────
RESOURCE_GROUP=$(parse_hcl_value "$TFVARS_FILE" "resource_group_name")
LOCATION=$(parse_hcl_value "$TFVARS_FILE" "location")
ENVIRONMENT=$(parse_hcl_value "$TFVARS_FILE" "environment")

# ── backend.hcl ──────────────────────────────────────────────────────────────
BACKEND_HCL="$TERRAFORM_DIR/backend.hcl"

if [[ ! -f "$BACKEND_HCL" ]]; then
    sanitized=$(echo "$RESOURCE_GROUP" | sed 's/^rg-//' | tr -cd 'a-z0-9')
    STORAGE_ACCOUNT="st${sanitized}"
    STORAGE_ACCOUNT="${STORAGE_ACCOUNT:0:24}"

    cat > "$BACKEND_HCL" <<EOF
resource_group_name  = "$RESOURCE_GROUP"
storage_account_name = "$STORAGE_ACCOUNT"
container_name       = "tfstate"
key                  = "$ENVIRONMENT.tfstate"
EOF
    done_ "Generated $BACKEND_HCL (storage account: $STORAGE_ACCOUNT)"
else
    skip_ "Using existing $BACKEND_HCL"
fi

STORAGE_ACCOUNT=$(parse_hcl_value "$BACKEND_HCL" "storage_account_name")
CONTAINER_NAME=$(parse_hcl_value "$BACKEND_HCL" "container_name")

# PHASE 2 — Create Azure backend resources
# ═══════════════════════════════════════════════════════════════════════════════

phase 2 "Create Azure backend resources"

echo ""
echo "    Resource Group:   $RESOURCE_GROUP"
echo "    Storage Account:  $STORAGE_ACCOUNT"
echo "    Container:        $CONTAINER_NAME"
echo "    Location:         $LOCATION"
echo ""

# ── Resource group ───────────────────────────────────────────────────────────
if ! az group show --name "$RESOURCE_GROUP" &>/dev/null; then
    az group create --name "$RESOURCE_GROUP" --location "$LOCATION" >/dev/null
    done_ "Created resource group $RESOURCE_GROUP"
else
    skip_ "Resource group $RESOURCE_GROUP already exists"
fi

# ── Storage account + container ──────────────────────────────────────────────
WAIT_TIME=30

if ! az storage account show --resource-group "$RESOURCE_GROUP" --name "$STORAGE_ACCOUNT" &>/dev/null; then
    echo "    Creating storage account $STORAGE_ACCOUNT..."
    az storage account create \
        --resource-group "$RESOURCE_GROUP" \
        --name "$STORAGE_ACCOUNT" \
        --sku "Standard_LRS" \
        --encryption-services blob \
        --min-tls-version "TLS1_2" \
        --location "$LOCATION" \
        --public-network-access Enabled >/dev/null
    echo "    Waiting ${WAIT_TIME}s for storage account to be ready..."
    sleep $WAIT_TIME
    done_ "Created storage account $STORAGE_ACCOUNT"
else
    skip_ "Storage account $STORAGE_ACCOUNT already exists"
    az storage account update --name "$STORAGE_ACCOUNT" --resource-group "$RESOURCE_GROUP" --public-network-access Enabled >/dev/null
    echo "    Enabled public network access. Waiting ${WAIT_TIME}s..."
    sleep $WAIT_TIME
fi

if ! az storage container show --name "$CONTAINER_NAME" --account-name "$STORAGE_ACCOUNT" --auth-mode login &>/dev/null; then
    az storage container create --name "$CONTAINER_NAME" --account-name "$STORAGE_ACCOUNT" --auth-mode login >/dev/null
    done_ "Created container $CONTAINER_NAME"
else
    skip_ "Container $CONTAINER_NAME already exists"
fi

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 3 — Entra app registration + OIDC
# ═══════════════════════════════════════════════════════════════════════════════

if [[ -z "$SUBSCRIPTION_ID" ]]; then
    SUBSCRIPTION_ID=$(az account show --query id -o tsv)
fi
if [[ -z "$GITHUB_REPO" ]]; then
    GITHUB_REPO=$(gh repo view --json nameWithOwner -q ".nameWithOwner" 2>/dev/null || true)
    if [[ -z "$GITHUB_REPO" ]]; then
        echo "Could not detect GitHub repo. Pass --repo 'owner/repo'."; exit 1
    fi
fi
REPO_NAME="${GITHUB_REPO##*/}"
if [[ -z "$APP_NAME" ]]; then APP_NAME="github-actions-${REPO_NAME}"; fi
TENANT_ID=$(az account show --query tenantId -o tsv)

if [[ "$SKIP_ENTRA" == "true" ]]; then
    if [[ -z "$APP_CLIENT_ID" ]]; then echo "--skip-entra requires --app-client-id"; exit 1; fi
    skip_ "Skipping Entra setup (using existing app: $APP_CLIENT_ID)"
else
    phase 3 "Entra app registration + OIDC"

    existing=$(az ad app list --display-name "$APP_NAME" --query "[0].appId" -o tsv 2>/dev/null || true)
    if [[ -n "$existing" ]]; then
        APP_CLIENT_ID="$existing"
        skip_ "App '$APP_NAME' already exists: $APP_CLIENT_ID"
    else
        APP_CLIENT_ID=$(az ad app create --display-name "$APP_NAME" --query appId -o tsv)
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
    CRED_NAME="github-actions-${GITHUB_ENV}"
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
        done_ "Added federated credential for repo:${GITHUB_REPO}:environment:${GITHUB_ENV}"
    fi

    step "Granting Contributor role on subscription"
    role_exists=$(az role assignment list --assignee "$APP_CLIENT_ID" --role "Contributor" --scope "/subscriptions/$SUBSCRIPTION_ID" --query "[0].id" -o tsv 2>/dev/null || true)
    if [[ -n "$role_exists" ]]; then
        skip_ "Contributor role already assigned"
    else
        az role assignment create --assignee "$APP_CLIENT_ID" --role "Contributor" --scope "/subscriptions/$SUBSCRIPTION_ID" >/dev/null
        done_ "Granted Contributor on subscription $SUBSCRIPTION_ID"
    fi
fi

# PHASE 4 — GitHub secrets + environment variables
# ═══════════════════════════════════════════════════════════════════════════════

phase 4 "GitHub secrets + environment variables"

step "Setting repository secrets"

gh secret set AZURE_CLIENT_ID --repo "$GITHUB_REPO" --body "$APP_CLIENT_ID"
done_ "AZURE_CLIENT_ID"
gh secret set AZURE_TENANT_ID --repo "$GITHUB_REPO" --body "$TENANT_ID"
done_ "AZURE_TENANT_ID"
gh secret set AZURE_SUBSCRIPTION_ID --repo "$GITHUB_REPO" --body "$SUBSCRIPTION_ID"
done_ "AZURE_SUBSCRIPTION_ID"

step "Setting environment variables ($GITHUB_ENV)"

# Read all values from terraform.tfvars — single source of truth
declare -A ENV_VARS=(
    # Backend / bootstrap
    [RESOURCE_GROUP]="$RESOURCE_GROUP"
    [LOCATION]="$LOCATION"
    [STORAGE_ACCOUNT]="$STORAGE_ACCOUNT"
    [STORAGE_ACCOUNT_SKU]="Standard_LRS"
    [STORAGE_ACCOUNT_ENCRYPTION_SERVICES]="blob"
    [STORAGE_ACCOUNT_MIN_TLS_VERSION]="TLS1_2"
    [STORAGE_ACCOUNT_PUBLIC_NETWORK_ACCESS]="Enabled"
    [TERRAFORM_STATE_CONTAINER]="$CONTAINER_NAME"
    [TERRAFORM_STATE_BLOB]="${ENVIRONMENT}.tfstate"
    [TERRAFORM_WORKING_DIRECTORY]="infra/terraform"

    # Infrastructure (read from terraform.tfvars)
    [TAGS]="{}"
    [ENVIRONMENT]="$ENVIRONMENT"
    [BASE_NAME]="$(parse_hcl_value "$TFVARS_FILE" "base_name")"
    [COGNITIVE_ACCOUNT_KIND]="$(parse_hcl_value "$TFVARS_FILE" "cognitive_account_kind")"
    [OAI_SKU_NAME]="$(parse_hcl_value "$TFVARS_FILE" "oai_sku_name")"
    [OAI_DEPLOYMENT_SKU_NAME]="$(parse_hcl_value "$TFVARS_FILE" "oai_deployment_sku_name")"
    [OAI_DEPLOYMENT_MODEL_FORMAT]="$(parse_hcl_value "$TFVARS_FILE" "oai_deployment_model_format")"
    [OAI_DEPLOYMENT_MODEL_NAME]="$(parse_hcl_value "$TFVARS_FILE" "oai_deployment_model_name")"
    [OAI_DEPLOYMENT_MODEL_VERSION]="$(parse_hcl_value "$TFVARS_FILE" "oai_deployment_model_version")"
    [OAI_VERSION_UPGRADE_OPTION]="$(parse_hcl_value "$TFVARS_FILE" "oai_version_upgrade_option")"
    [CREATE_EMBEDDING_DEPLOYMENT]="$(parse_hcl_value "$TFVARS_FILE" "create_embedding_deployment")"
    [EMBEDDING_MODEL_NAME]="$(parse_hcl_value "$TFVARS_FILE" "embedding_model_name")"
    [EMBEDDING_MODEL_VERSION]="$(parse_hcl_value "$TFVARS_FILE" "embedding_model_version")"
    [EMBEDDING_SKU_NAME]="$(parse_hcl_value "$TFVARS_FILE" "embedding_sku_name")"
    [EMBEDDING_CAPACITY]="$(parse_hcl_value "$TFVARS_FILE" "embedding_capacity")"
    [COSMOS_PROJECT_NAME]="$(parse_hcl_value "$TFVARS_FILE" "cosmos_project_name")"
    [COSMOS_AGENTS_DATABASE_NAME]="$(parse_hcl_value "$TFVARS_FILE" "cosmos_agents_database_name")"
    [COSMOS_AGENT_STATE_CONTAINER_NAME]="$(parse_hcl_value "$TFVARS_FILE" "cosmos_agent_state_container_name")"
    [SQL_DATABASE_NAME]="$(parse_hcl_value "$TFVARS_FILE" "sql_database_name")"
    [SQL_ADMIN_LOGIN]="$(parse_hcl_value "$TFVARS_FILE" "sql_admin_login")"
    [STORAGE_PROJECT_NAME]="$(parse_hcl_value "$TFVARS_FILE" "storage_project_name")"
    [SEARCH_SKU]="$(parse_hcl_value "$TFVARS_FILE" "search_sku")"
    [SEARCH_INDEX_NAME]="$(parse_hcl_value "$TFVARS_FILE" "search_index_name")"
    [ACR_PROJECT_NAME]="$(parse_hcl_value "$TFVARS_FILE" "acr_project_name")"
    [CREATE_ACR]="$(parse_hcl_value "$TFVARS_FILE" "create_acr")"
    [ACR_SKU]="$(parse_hcl_value "$TFVARS_FILE" "acr_sku")"
    [EXISTING_ACR_NAME]=""
    [AKS_KUBERNETES_VERSION]=""
    [AKS_NODE_VM_SIZE]="$(parse_hcl_value "$TFVARS_FILE" "aks_node_vm_size")"
    [AKS_NODE_COUNT]="$(parse_hcl_value "$TFVARS_FILE" "aks_node_count")"
    [AKS_AUTO_SCALING_ENABLED]="$(parse_hcl_value "$TFVARS_FILE" "aks_auto_scaling_enabled")"
    [AKS_NODE_MIN_COUNT]="$(parse_hcl_value "$TFVARS_FILE" "aks_node_min_count")"
    [AKS_NODE_MAX_COUNT]="$(parse_hcl_value "$TFVARS_FILE" "aks_node_max_count")"
    [AKS_OS_DISK_SIZE_GB]="$(parse_hcl_value "$TFVARS_FILE" "aks_os_disk_size_gb")"
    [AKS_LOG_RETENTION_DAYS]="$(parse_hcl_value "$TFVARS_FILE" "aks_log_retention_days")"
)

count=0
for key in "${!ENV_VARS[@]}"; do
    gh variable set "$key" --repo "$GITHUB_REPO" --env "$GITHUB_ENV" --body "${ENV_VARS[$key]}"
    count=$((count + 1))
done

done_ "Set $count environment variables"

# PHASE 5 — Lock down storage
# ═══════════════════════════════════════════════════════════════════════════════

phase 5 "Lock down state storage"

az storage account update --name "$STORAGE_ACCOUNT" --resource-group "$RESOURCE_GROUP" --public-network-access Disabled >/dev/null
done_ "Public access disabled on $STORAGE_ACCOUNT"

# ── Summary ──────────────────────────────────────────────────────────────────
echo ""
echo -e "  ${G}╔═══════════════════════════════════════════════════════╗${W}"
echo -e "  ${G}║  Bootstrap Complete!                                  ║${W}"
echo -e "  ${G}╠═══════════════════════════════════════════════════════╣${W}"
echo -e "  ${G}║${W}  Resource group:   $RESOURCE_GROUP"
echo -e "  ${G}║${W}  Storage account:  $STORAGE_ACCOUNT"
echo -e "  ${G}║${W}  App registration: $APP_CLIENT_ID"
echo -e "  ${G}║${W}  GitHub repo:      $GITHUB_REPO"
echo -e "  ${G}║${W}  GitHub env:       $GITHUB_ENV"
echo -e "  ${G}║${W}  Secrets:          3 (AZURE_CLIENT_ID, TENANT_ID, SUBSCRIPTION_ID)"
echo -e "  ${G}║${W}  Env variables:    $count"
echo -e "  ${G}║${W}"
echo -e "  ${G}║  Next: proceed to Lab 1 (terraform apply)             ║${W}"
echo -e "  ${G}╚═══════════════════════════════════════════════════════╝${W}"
echo ""
