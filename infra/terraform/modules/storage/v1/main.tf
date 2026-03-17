# =============================================================================
# Azure Storage Account Module v1
# Creates: Storage Account + N Blob Containers (control plane only)
# Blob uploads are handled by the storage-uploads module (data plane).
# =============================================================================

locals {
  # Storage account names: 3-24 chars, lowercase alphanumeric only
  storage_account_name = substr(replace("st${var.base_name}${var.environment}${var.location}", "-", ""), 0, 24)
}

# -----------------------------------------------------------------------------
# Storage Account
# -----------------------------------------------------------------------------
resource "azurerm_storage_account" "this" {
  name                     = local.storage_account_name
  resource_group_name      = var.resource_group_name
  location                 = var.location
  account_tier             = var.account_tier
  account_replication_type = var.replication_type

  allow_nested_items_to_be_public = var.allow_public_access

  tags = var.tags

  lifecycle {
    ignore_changes = [tags]
  }
}

# -----------------------------------------------------------------------------
# Blob Containers (one per entry in var.containers)
# -----------------------------------------------------------------------------
resource "azurerm_storage_container" "this" {
  for_each = var.containers

  name                  = each.value.name
  storage_account_id    = azurerm_storage_account.this.id
  container_access_type = each.value.access_type
}

