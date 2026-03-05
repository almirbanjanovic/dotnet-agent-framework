# =============================================================================
# RBAC - Cosmos DB Module v1
# Assigns: Cosmos DB SQL Data Owner + Data Contributor roles
# =============================================================================

# Built-in Cosmos DB SQL role definition IDs:
#   Data Owner:       00000000-0000-0000-0000-000000000001
#   Data Contributor: 00000000-0000-0000-0000-000000000002

resource "azurerm_cosmosdb_sql_role_assignment" "data_owner" {
  for_each = var.principal_ids

  resource_group_name = var.resource_group_name
  account_name        = var.cosmosdb_account_name
  role_definition_id  = "${var.cosmosdb_account_id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000001"
  principal_id        = each.value
  scope               = var.cosmosdb_account_id
}

resource "azurerm_cosmosdb_sql_role_assignment" "data_contributor" {
  for_each = var.principal_ids

  resource_group_name = var.resource_group_name
  account_name        = var.cosmosdb_account_name
  role_definition_id  = "${var.cosmosdb_account_id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002"
  principal_id        = each.value
  scope               = var.cosmosdb_account_id
}
