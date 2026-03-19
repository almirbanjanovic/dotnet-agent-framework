output "id" {
  description = "Azure AI Search service resource ID"
  value       = azurerm_search_service.this.id
}

output "name" {
  description = "Azure AI Search service name"
  value       = azurerm_search_service.this.name
}

output "endpoint" {
  description = "Azure AI Search endpoint URL"
  value       = "https://${azurerm_search_service.this.name}.search.windows.net"
}

output "primary_key" {
  description = "Azure AI Search primary admin key"
  value       = data.azapi_resource_action.list_admin_keys.output.primaryKey
  sensitive   = true
}

output "identity_principal_id" {
  description = "Principal ID of the search service system-assigned managed identity"
  value       = azurerm_search_service.this.identity[0].principal_id
}
