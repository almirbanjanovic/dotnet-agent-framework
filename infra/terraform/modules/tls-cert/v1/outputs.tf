output "certificate_id" {
  description = "Key Vault certificate resource ID"
  value       = azurerm_key_vault_certificate.tls.id
}

output "secret_id" {
  description = "Key Vault secret ID for the certificate (used by AGC TLS)"
  value       = azurerm_key_vault_certificate.tls.secret_id
}

output "versionless_secret_id" {
  description = "Versionless Key Vault secret ID (used by AGC TLS)"
  value       = azurerm_key_vault_certificate.tls.versionless_secret_id
}
