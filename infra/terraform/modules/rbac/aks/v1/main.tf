# =============================================================================
# RBAC - AKS Control Plane Module v1
# Assigns: Contributor role on the resource group for AKS control plane identity
# =============================================================================

data "azurerm_resource_group" "this" {
  name = var.resource_group_name
}

resource "azurerm_role_assignment" "aks_contributor" {
  scope                = data.azurerm_resource_group.this.id
  role_definition_name = "Contributor"
  principal_id         = var.aks_control_plane_principal_id
}
