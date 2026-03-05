# =============================================================================
# RBAC - ACR Module v1
# Assigns: AcrPull role to workload identities
# =============================================================================

resource "azurerm_role_assignment" "acr_pull" {
  for_each = var.principal_ids

  scope                = var.acr_id
  role_definition_name = "AcrPull"
  principal_id         = each.value
}
