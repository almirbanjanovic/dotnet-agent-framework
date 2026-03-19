# =============================================================================
# Key Vault Secrets Module v1
# Writes a map of secrets to an Azure Key Vault
# =============================================================================

resource "azurerm_key_vault_secret" "this" {
  for_each = var.secrets

  name         = each.key
  value        = each.value
  key_vault_id = var.key_vault_id
}
