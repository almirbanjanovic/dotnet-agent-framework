# =============================================================================
# RBAC - Foundry (AI Services) Module v1
# Assigns: Cognitive Services OpenAI User + Cognitive Services User roles
#          to workload identities
# =============================================================================

resource "azurerm_role_assignment" "openai_user" {
  for_each = var.principal_ids

  scope                = var.ai_services_account_id
  role_definition_name = "Cognitive Services OpenAI User"
  principal_id         = each.value
}

resource "azurerm_role_assignment" "cognitive_services_user" {
  for_each = var.principal_ids

  scope                = var.ai_services_account_id
  role_definition_name = "Cognitive Services User"
  principal_id         = each.value
}
