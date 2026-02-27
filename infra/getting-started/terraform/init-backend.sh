#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

#-------------------------------------------------------
# Parse values from backend.hcl and terraform.tfvars
#-------------------------------------------------------
parse_hcl_value() {
  local file="$1" key="$2"
  grep -E "^\s*${key}\s*=" "$file" | head -1 | sed 's/.*=\s*"\(.*\)".*/\1/'
}

RESOURCE_GROUP=$(parse_hcl_value "$SCRIPT_DIR/backend.hcl" "resource_group_name")
STORAGE_ACCOUNT=$(parse_hcl_value "$SCRIPT_DIR/backend.hcl" "storage_account_name")
CONTAINER_NAME=$(parse_hcl_value "$SCRIPT_DIR/backend.hcl" "container_name")
LOCATION=$(parse_hcl_value "$SCRIPT_DIR/terraform.tfvars" "location")

# Storage account defaults (match CI/CD workflow)
STORAGE_ACCOUNT_SKU="Standard_LRS"
STORAGE_ACCOUNT_ENCRYPTION_SERVICES="blob"
STORAGE_ACCOUNT_MIN_TLS_VERSION="TLS1_2"

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
