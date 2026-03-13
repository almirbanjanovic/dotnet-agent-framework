# =============================================================================
# Azure Storage Account Module v1
# Creates: Storage Account + N Blob Containers + uploads files per container
# =============================================================================

locals {
  # Storage account names: 3-24 chars, lowercase alphanumeric only
  storage_account_name = substr(replace("st${var.base_name}${var.environment}${var.location}", "-", ""), 0, 24)

  # Flatten containers × files into a single map for blob uploads
  blob_uploads = merge([
    for key, container in var.containers : {
      for file in (container.upload_source_path != "" ? fileset(container.upload_source_path, container.upload_file_pattern) : []) :
      "${key}/${file}" => {
        container_name = container.name
        file_name      = file
        source_path    = "${container.upload_source_path}/${file}"
        content_type   = container.upload_content_type
      }
    }
  ]...)
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

# -----------------------------------------------------------------------------
# Upload files (flattened across all containers)
# -----------------------------------------------------------------------------
resource "azurerm_storage_blob" "uploads" {
  for_each = local.blob_uploads

  name                   = each.value.file_name
  storage_account_name   = azurerm_storage_account.this.name
  storage_container_name = each.value.container_name
  type                   = "Block"
  source                 = each.value.source_path
  content_type           = each.value.content_type

  depends_on = [azurerm_storage_container.this]
}

# -----------------------------------------------------------------------------
# State migration — moved blocks for existing resources
# -----------------------------------------------------------------------------
moved {
  from = azurerm_storage_container.this
  to   = azurerm_storage_container.this["images"]
}

moved {
  from = azurerm_storage_blob.images
  to   = azurerm_storage_blob.uploads
}

