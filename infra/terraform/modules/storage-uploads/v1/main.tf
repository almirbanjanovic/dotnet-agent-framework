# =============================================================================
# Storage Uploads Module v1
# Uploads files to existing blob containers (data plane only).
# Uses Azure AD authentication (storage_account_id) because the storage
# account has shared_access_key_enabled = false (MCAPS policy requirement).
# =============================================================================

# Grant the deployer Storage Blob Data Contributor so Terraform can upload blobs
data "azurerm_client_config" "current" {}

resource "azurerm_role_assignment" "deployer_blob_contributor" {
  scope                = var.storage_account_id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = data.azurerm_client_config.current.object_id
}

locals {
  # Flatten uploads × files into a single map for azurerm_storage_blob
  blob_uploads = merge([
    for key, upload in var.uploads : {
      for file in fileset(upload.source_path, upload.file_pattern) :
      "${key}/${file}" => {
        container_name = upload.container_name
        file_name      = file
        source_path    = "${upload.source_path}/${file}"
        content_type   = upload.content_type
      }
    }
  ]...)
}

resource "azurerm_storage_blob" "this" {
  for_each = local.blob_uploads

  name                   = each.value.file_name
  storage_account_name   = var.storage_account_name
  storage_container_name = each.value.container_name
  type                   = "Block"
  source                 = each.value.source_path
  content_type           = each.value.content_type

  depends_on = [azurerm_role_assignment.deployer_blob_contributor]
}
