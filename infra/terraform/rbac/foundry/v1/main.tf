# =============================================================================
# RBAC - Foundry (AI Services) Module v1
# Assigns: Cognitive Services OpenAI User role to workload identities
# =============================================================================

resource "azurerm_role_assignment" "openai_user" {
  for_each = var.principal_ids

  scope                = var.ai_services_account_id
  role_definition_name = "Cognitive Services OpenAI User"
  principal_id         = each.value
}
