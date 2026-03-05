# =============================================================================
# Identity Module v1
# Creates: User-assigned managed identities for workloads
# =============================================================================

resource "azurerm_user_assigned_identity" "workload" {
  for_each = var.identities

  name                = each.value.name
  resource_group_name = var.resource_group_name
  location            = var.location
  tags                = var.tags
}
