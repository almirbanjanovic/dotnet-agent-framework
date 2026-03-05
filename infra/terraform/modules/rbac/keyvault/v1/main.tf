# =============================================================================
# RBAC - Key Vault Module v1
# Assigns: Key Vault Secrets Officer (for Terraform to write secrets)
#          Key Vault Secrets User (for workloads to read secrets)
# =============================================================================

# Secrets Officer — allows writing/managing secrets (for Terraform deployer)
resource "azurerm_role_assignment" "secrets_officer" {
  for_each = var.officer_principal_ids

  scope                = var.keyvault_id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = each.value
}

# Secrets User — allows reading secrets (for workload identities and developers)
resource "azurerm_role_assignment" "secrets_user" {
  for_each = var.reader_principal_ids

  scope                = var.keyvault_id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = each.value
}
