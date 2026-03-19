# =============================================================================
# Key Vault Secrets Module v1
# Writes a map of secrets to an Azure Key Vault
# =============================================================================

resource "azurerm_key_vault_secret" "this" {
  # nonsensitive() on the whole map is safe here — only the KEYS are used for
  # resource addressing (for_each). The secret VALUES remain sensitive via the
  # provider (azurerm_key_vault_secret marks value as sensitive internally).
  for_each = nonsensitive(var.secrets)

  name         = each.key
  value        = each.value
  key_vault_id = var.key_vault_id
}
