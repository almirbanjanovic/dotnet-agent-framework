# =============================================================================
# RBAC - Foundry (AI Services) Module v1
# Assigns workload identities the roles needed for the new Foundry experience:
#   * Cognitive Services OpenAI User    (account scope) — chat & embeddings
#   * Cognitive Services User           (account scope) — deployments listing
#   * Azure AI User                     (project scope) — AIProjectClient,
#     agents, threads, memory stores, evaluations, and other project-plane APIs
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

resource "azurerm_role_assignment" "ai_user" {
  for_each = var.principal_ids

  scope                = var.foundry_project_id
  role_definition_name = "Azure AI User"
  principal_id         = each.value
}
