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
  value       = azurerm_search_service.this.primary_key
  sensitive   = true
}

output "identity_principal_id" {
  description = "Principal ID of the search service system-assigned managed identity"
  value       = azurerm_search_service.this.identity[0].principal_id
}

output "indexer_name" {
  description = "Name of the blob indexer (created in root main.tf after RBAC)"
  value       = "blob-indexer"
}

output "index_name" {
  description = "Name of the search index"
  value       = var.index_name
}

output "data_source_name" {
  description = "Name of the blob data source"
  value       = azapi_resource.search_data_source.name
}

output "skillset_name" {
  description = "Name of the vectorize skillset"
  value       = azapi_resource.search_skillset.name
}
