# =============================================================================
# RBAC - AI Search Module v1
# Assigns: Search Index Data Reader role to workload identities
# =============================================================================

resource "azurerm_role_assignment" "search_index_data_reader" {
  for_each = var.principal_ids

  scope                = var.search_service_id
  role_definition_name = "Search Index Data Reader"
  principal_id         = each.value
}
