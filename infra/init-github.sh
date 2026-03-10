#!/usr/bin/env bash
set -euo pipefail

# ═══════════════════════════════════════════════════════════════════════════════
# One-time bootstrap: creates Entra app registration with OIDC for GitHub Actions
# and configures GitHub repository secrets + environment variables.
#
# Usage:
#   ./init-github.sh
#   ./init-github.sh --subscription "12345678-..." --repo "myorg/myrepo"
#   ./init-github.sh --skip-entra --app-client-id "12345678-..."
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

# ── Helpers ──────────────────────────────────────────────────────────────────
step()  { echo -e "\n\033[36m==> $1\033[0m"; }
done_() { echo -e "    \033[32m✓ $1\033[0m"; }
skip_() { echo -e "    \033[33m⊘ $1\033[0m"; }

# ── Verify prerequisites ────────────────────────────────────────────────────
step "Checking prerequisites"

command -v az >/dev/null 2>&1 || { echo "Azure CLI (az) is not installed."; exit 1; }
command -v gh >/dev/null 2>&1 || { echo "GitHub CLI (gh) is not installed."; exit 1; }

az account show >/dev/null 2>&1 || { echo "Not logged in to Azure CLI. Run 'az login' first."; exit 1; }
gh auth status >/dev/null 2>&1 || { echo "Not logged in to GitHub CLI. Run 'gh auth login' first."; exit 1; }

done_ "az and gh authenticated"

# ── Resolve defaults ────────────────────────────────────────────────────────
if [[ -z "$SUBSCRIPTION_ID" ]]; then
    SUBSCRIPTION_ID=$(az account show --query id -o tsv)
    done_ "Using current subscription: $SUBSCRIPTION_ID"
fi

if [[ -z "$GITHUB_REPO" ]]; then
    GITHUB_REPO=$(gh repo view --json nameWithOwner -q ".nameWithOwner" 2>/dev/null || true)
    if [[ -z "$GITHUB_REPO" ]]; then
        echo "Could not detect GitHub repo. Pass --repo 'owner/repo' or run from inside a git repo."
        exit 1
    fi
    done_ "Using current repo: $GITHUB_REPO"
fi

REPO_NAME="${GITHUB_REPO##*/}"

if [[ -z "$APP_NAME" ]]; then
    APP_NAME="github-actions-${REPO_NAME}"
fi

TENANT_ID=$(az account show --query tenantId -o tsv)

echo ""
echo "╔═══════════════════════════════════════════════════════════╗"
echo "║  Bootstrap Configuration                                 ║"
echo "╠═══════════════════════════════════════════════════════════╣"
echo "║  Subscription:   $SUBSCRIPTION_ID"
echo "║  Tenant:         $TENANT_ID"
echo "║  GitHub repo:    $GITHUB_REPO"
echo "║  GitHub env:     $GITHUB_ENV"
echo "║  App name:       $APP_NAME"
echo "║  Skip Entra:     $SKIP_ENTRA"
echo "╚═══════════════════════════════════════════════════════════╝"

# ── Step 1: Entra app registration ──────────────────────────────────────────
if [[ "$SKIP_ENTRA" == "true" ]]; then
    if [[ -z "$APP_CLIENT_ID" ]]; then
        echo "--skip-entra requires --app-client-id"; exit 1
    fi
    skip_ "Skipping Entra setup (using existing app: $APP_CLIENT_ID)"
else
    step "Creating Entra app registration"

    existing=$(az ad app list --display-name "$APP_NAME" --query "[0].appId" -o tsv 2>/dev/null || true)
    if [[ -n "$existing" ]]; then
        APP_CLIENT_ID="$existing"
        skip_ "App '$APP_NAME' already exists: $APP_CLIENT_ID"
    else
        APP_CLIENT_ID=$(az ad app create --display-name "$APP_NAME" --query appId -o tsv)
        done_ "Created app: $APP_CLIENT_ID"
    fi

    # Create service principal if not exists
    sp_exists=$(az ad sp show --id "$APP_CLIENT_ID" --query id -o tsv 2>/dev/null || true)
    if [[ -n "$sp_exists" ]]; then
        skip_ "Service principal already exists"
    else
        az ad sp create --id "$APP_CLIENT_ID" >/dev/null
        done_ "Created service principal"
    fi

    # Add OIDC federated credential
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

    # Grant Contributor
    step "Granting Contributor role on subscription"
    role_exists=$(az role assignment list --assignee "$APP_CLIENT_ID" --role "Contributor" --scope "/subscriptions/$SUBSCRIPTION_ID" --query "[0].id" -o tsv 2>/dev/null || true)
    if [[ -n "$role_exists" ]]; then
        skip_ "Contributor role already assigned"
    else
        az role assignment create --assignee "$APP_CLIENT_ID" --role "Contributor" --scope "/subscriptions/$SUBSCRIPTION_ID" >/dev/null
        done_ "Granted Contributor on subscription $SUBSCRIPTION_ID"
    fi
fi

# ── Step 2: GitHub repository secrets ────────────────────────────────────────
step "Setting GitHub repository secrets"

gh secret set AZURE_CLIENT_ID --repo "$GITHUB_REPO" --body "$APP_CLIENT_ID"
done_ "AZURE_CLIENT_ID"

gh secret set AZURE_TENANT_ID --repo "$GITHUB_REPO" --body "$TENANT_ID"
done_ "AZURE_TENANT_ID"

gh secret set AZURE_SUBSCRIPTION_ID --repo "$GITHUB_REPO" --body "$SUBSCRIPTION_ID"
done_ "AZURE_SUBSCRIPTION_ID"

# ── Step 3: GitHub environment variables ─────────────────────────────────────
step "Setting GitHub environment variables ($GITHUB_ENV)"

declare -A ENV_VARS=(
    # Backend / bootstrap
    [RESOURCE_GROUP]="rg-agentic-ai-centralus"
    [LOCATION]="centralus"
    [STORAGE_ACCOUNT]="stagenticaicentralus"
    [STORAGE_ACCOUNT_SKU]="Standard_LRS"
    [STORAGE_ACCOUNT_ENCRYPTION_SERVICES]="blob"
    [STORAGE_ACCOUNT_MIN_TLS_VERSION]="TLS1_2"
    [STORAGE_ACCOUNT_PUBLIC_NETWORK_ACCESS]="Enabled"
    [TERRAFORM_STATE_CONTAINER]="tfstate"
    [TERRAFORM_STATE_BLOB]="agentic-ai.tfstate"
    [TERRAFORM_WORKING_DIRECTORY]="infra/terraform"

    # Infrastructure (mapped to TF_VAR_ in workflows)
    [TAGS]="{}"
    [ENVIRONMENT]="dev"
    [BASE_NAME]="agentic-ai"
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
    [COSMOS_PROJECT_NAME]="dotnetagent"
    [COSMOS_OPERATIONAL_DATABASE_NAME]="contoso-outdoors"
    [COSMOS_KNOWLEDGE_DATABASE_NAME]="knowledge"
    [COSMOS_AGENTS_DATABASE_NAME]="agents"
    [COSMOS_AGENT_STATE_CONTAINER_NAME]="workshop_agent_state_store"
    [STORAGE_PROJECT_NAME]="dotnetagent"
    [STORAGE_IMAGES_CONTAINER_NAME]="product-images"
    [ACR_PROJECT_NAME]="dotnetagent"
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

done_ "Set $count environment variables"

# ── Summary ──────────────────────────────────────────────────────────────────
echo ""
echo "╔═══════════════════════════════════════════════════════════╗"
echo "║  Bootstrap Complete                                      ║"
echo "╠═══════════════════════════════════════════════════════════╣"
echo "║  App registration:  $APP_CLIENT_ID"
echo "║  Tenant ID:         $TENANT_ID"
echo "║  Subscription ID:   $SUBSCRIPTION_ID"
echo "║  GitHub repo:       $GITHUB_REPO"
echo "║  GitHub env:        $GITHUB_ENV"
echo "║  Repo secrets:      3 (AZURE_CLIENT_ID, TENANT_ID, SUBSCRIPTION_ID)"
echo "║  Env variables:     $count"
echo "╚═══════════════════════════════════════════════════════════╝"
echo ""
echo "Next steps:"
echo "  1. Run infra/init-backend.sh to bootstrap Terraform state"
echo "  2. Proceed to Lab 1"
