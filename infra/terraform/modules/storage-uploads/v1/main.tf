# =============================================================================
# Storage Uploads Module v1
# Uploads files to existing blob containers (data plane only).
# Separated from storage account creation so uploads can be ordered after
# containers exist and independently of other control plane resources.
# =============================================================================

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
}
