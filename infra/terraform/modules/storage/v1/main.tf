# =============================================================================
# Azure Storage Account Module v1
# Creates: Storage Account + Blob Container + uploads product images
# =============================================================================

locals {
  # Storage account names: 3-24 chars, lowercase alphanumeric only
  storage_account_name = replace("st${var.project_name}${var.purpose}", "-", "")
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
# Blob Container
# -----------------------------------------------------------------------------
resource "azurerm_storage_container" "this" {
  name                  = var.container_name
  storage_account_id    = azurerm_storage_account.this.id
  container_access_type = var.container_access_type
}

# -----------------------------------------------------------------------------
# Upload image files
# -----------------------------------------------------------------------------
resource "azurerm_storage_blob" "images" {
  for_each = var.image_source_path != "" ? fileset(var.image_source_path, "*.png") : toset([])

  name                   = each.value
  storage_account_name   = azurerm_storage_account.this.name
  storage_container_name = azurerm_storage_container.this.name
  type                   = "Block"
  source                 = "${var.image_source_path}/${each.value}"
  content_type           = "image/png"

  depends_on = [azurerm_storage_container.this]
}
