#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TERRAFORM_DIR="$SCRIPT_DIR/terraform"

#-------------------------------------------------------
# Parse HCL values helper
#-------------------------------------------------------
parse_hcl_value() {
  local file="$1" key="$2"
  grep -E "^\s*${key}\s*=" "$file" | head -1 | sed 's/.*=\s*"\(.*\)".*/\1/'
}

#-------------------------------------------------------
# Auto-generate terraform.tfvars if it doesn't exist
#-------------------------------------------------------
TFVARS_FILE="$TERRAFORM_DIR/terraform.tfvars"

if [[ ! -f "$TFVARS_FILE" ]]; then
  cat > "$TFVARS_FILE" <<'TFVARS'
tags                = {}
resource_group_name = "rg-agentic-ai-centralus"

environment = "dev"
base_name   = "agentic-ai"
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
# Cosmos DB (3 accounts: operational, knowledge, agents)
# ---------------------------------------------------------------
cosmos_project_name               = "dotnetagent"
cosmos_operational_database_name  = "contoso-outdoors"
cosmos_knowledge_database_name    = "knowledge"
cosmos_agents_database_name       = "agents"
cosmos_agent_state_container_name = "workshop_agent_state_store"

# ---------------------------------------------------------------
# Storage (Product Images)
# ---------------------------------------------------------------
storage_project_name          = "dotnetagent"
storage_images_container_name = "product-images"

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
  echo "Generated $TFVARS_FILE with default values."
  echo "  (Edit this file to customize resource names, regions, or SKUs)"
  echo ""
else
  echo "Using existing $TFVARS_FILE"
fi

RESOURCE_GROUP=$(parse_hcl_value "$TFVARS_FILE" "resource_group_name")
LOCATION=$(parse_hcl_value "$TFVARS_FILE" "location")

#-------------------------------------------------------
# Auto-generate backend.hcl if it doesn't exist
#-------------------------------------------------------
BACKEND_HCL="$TERRAFORM_DIR/backend.hcl"

if [[ ! -f "$BACKEND_HCL" ]]; then
  # Derive storage account name: strip 'rg-' prefix, remove non-alphanumeric, prepend 'st'
  sanitized=$(echo "$RESOURCE_GROUP" | sed 's/^rg-//' | tr -cd 'a-z0-9')
  STORAGE_ACCOUNT="st${sanitized}"
  # Storage account names max 24 chars
  STORAGE_ACCOUNT="${STORAGE_ACCOUNT:0:24}"

  ENVIRONMENT=$(parse_hcl_value "$TERRAFORM_DIR/terraform.tfvars" "environment")

  cat > "$BACKEND_HCL" <<EOF
resource_group_name  = "$RESOURCE_GROUP"
storage_account_name = "$STORAGE_ACCOUNT"
container_name       = "tfstate"
key                  = "$ENVIRONMENT.tfstate"
EOF
  echo "Generated $BACKEND_HCL"
  echo "  Storage account name: $STORAGE_ACCOUNT"
  echo "  (Edit this file if you need a different storage account name)"
  echo ""
else
  echo "Using existing $BACKEND_HCL"
fi

STORAGE_ACCOUNT=$(parse_hcl_value "$BACKEND_HCL" "storage_account_name")
CONTAINER_NAME=$(parse_hcl_value "$BACKEND_HCL" "container_name")

# Storage account defaults (match CI/CD workflow)
STORAGE_ACCOUNT_SKU="Standard_LRS"
STORAGE_ACCOUNT_ENCRYPTION_SERVICES="blob"
STORAGE_ACCOUNT_MIN_TLS_VERSION="TLS1_2"

echo ""
echo "=== Terraform Backend Bootstrap ==="
echo "Resource Group:   $RESOURCE_GROUP"
echo "Storage Account:  $STORAGE_ACCOUNT"
echo "Container:        $CONTAINER_NAME"
echo "Location:         $LOCATION"
echo "===================================="

#-------------------------------------------------------
# Verify az login
#-------------------------------------------------------
if ! az account show &>/dev/null; then
  echo "ERROR: Not logged in to Azure CLI. Run 'az login' first."
  exit 1
fi

#-------------------------------------------------------
# Create resource group
#-------------------------------------------------------
if ! az group show --name "$RESOURCE_GROUP" &>/dev/null; then
  echo "Creating resource group $RESOURCE_GROUP..."
  az group create --name "$RESOURCE_GROUP" --location "$LOCATION"
else
  echo "Resource group $RESOURCE_GROUP already exists."
fi

#-------------------------------------------------------
# Create storage account
#-------------------------------------------------------
WAIT_TIME=30

disable_public_access() {
  echo "Disabling public network access..."
  az storage account update \
    --name "$STORAGE_ACCOUNT" \
    --resource-group "$RESOURCE_GROUP" \
    --public-network-access Disabled
}

trap disable_public_access EXIT

if ! az storage account show --resource-group "$RESOURCE_GROUP" --name "$STORAGE_ACCOUNT" &>/dev/null; then
  echo "Creating storage account $STORAGE_ACCOUNT..."
  az storage account create \
    --resource-group "$RESOURCE_GROUP" \
    --name "$STORAGE_ACCOUNT" \
    --sku "$STORAGE_ACCOUNT_SKU" \
    --encryption-services "$STORAGE_ACCOUNT_ENCRYPTION_SERVICES" \
    --min-tls-version "$STORAGE_ACCOUNT_MIN_TLS_VERSION" \
    --location "$LOCATION" \
    --public-network-access Enabled

  echo "Waiting ${WAIT_TIME}s for storage account to be ready..."
  sleep $WAIT_TIME
else
  echo "Storage account $STORAGE_ACCOUNT already exists."

  az storage account update \
    --name "$STORAGE_ACCOUNT" \
    --resource-group "$RESOURCE_GROUP" \
    --public-network-access Enabled

  echo "Enabled public network access. Waiting ${WAIT_TIME}s..."
  sleep $WAIT_TIME
fi

#-------------------------------------------------------
# Create blob container
#-------------------------------------------------------
if ! az storage container show --name "$CONTAINER_NAME" --account-name "$STORAGE_ACCOUNT" --auth-mode login &>/dev/null; then
  echo "Creating container $CONTAINER_NAME..."
  az storage container create --name "$CONTAINER_NAME" --account-name "$STORAGE_ACCOUNT" --auth-mode login
else
  echo "Container $CONTAINER_NAME already exists."
fi

echo "Backend bootstrap complete."
