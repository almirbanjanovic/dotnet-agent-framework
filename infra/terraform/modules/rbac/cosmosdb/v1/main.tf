# =============================================================================
# RBAC - Cosmos DB Module v1
# Assigns: Cosmos DB Built-in Data Contributor role
# Data Contributor allows read + write (upsert, delete) on documents.
# =============================================================================

# Built-in Cosmos DB SQL role definition IDs:
#   Data Reader:      00000000-0000-0000-0000-000000000001 (read only)
#   Data Contributor:  00000000-0000-0000-0000-000000000002 (read + write)

resource "azurerm_cosmosdb_sql_role_assignment" "data_contributor" {
  for_each = var.principal_ids

  resource_group_name = var.resource_group_name
  account_name        = var.cosmosdb_account_name
  role_definition_id  = "${var.cosmosdb_account_id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002"
  principal_id        = each.value
  scope               = var.cosmosdb_account_id
}
