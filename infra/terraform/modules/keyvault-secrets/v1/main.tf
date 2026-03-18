# =============================================================================
# Key Vault Secrets Module v1
# Writes a map of secrets to an Azure Key Vault
# =============================================================================

# Uses value_wo so secrets are never persisted in Terraform state.
# Secrets are force-rewritten on every apply via a random version integer
# that regenerates each plan (plantimestamp keeper changes every run).
resource "random_integer" "secrets_version" {
  min = 1
  max = 999999

  keepers = {
    always_run = plantimestamp()
  }
}

resource "azurerm_key_vault_secret" "this" {
  for_each = toset(nonsensitive(keys(var.secrets)))

  name             = each.key
  value_wo         = var.secrets[each.key]
  value_wo_version = random_integer.secrets_version.result
  key_vault_id     = var.key_vault_id
}
