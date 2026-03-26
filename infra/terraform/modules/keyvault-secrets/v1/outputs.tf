output "secret_ids" {
  description = "Map of secret name => secret resource ID"
  value       = { for k, v in azurerm_key_vault_secret.this : k => v.id }
}
