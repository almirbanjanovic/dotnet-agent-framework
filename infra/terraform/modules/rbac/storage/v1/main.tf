# =============================================================================
# RBAC - Storage Module v1
# Assigns: Storage Blob Data Reader role to workload identities
# =============================================================================

resource "azurerm_role_assignment" "blob_data_reader" {
  for_each = var.principal_ids

  scope                = var.storage_account_id
  role_definition_name = "Storage Blob Data Reader"
  principal_id         = each.value
}
