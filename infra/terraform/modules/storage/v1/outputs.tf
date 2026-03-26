output "id" {
  description = "Storage account resource ID"
  value       = azurerm_storage_account.this.id
}

output "name" {
  description = "Storage account name"
  value       = azurerm_storage_account.this.name
}

output "primary_blob_endpoint" {
  description = "Primary blob endpoint URL"
  value       = azurerm_storage_account.this.primary_blob_endpoint
}

output "container_names" {
  description = "Map of logical key => blob container name"
  value       = { for k, v in azurerm_storage_container.this : k => v.name }
}
