output "account_id" {
  description = "Cosmos DB account resource ID"
  value       = azurerm_cosmosdb_account.this.id
}

output "account_name" {
  description = "Cosmos DB account name"
  value       = azurerm_cosmosdb_account.this.name
}

output "endpoint" {
  description = "Cosmos DB endpoint URL"
  value       = azurerm_cosmosdb_account.this.endpoint
}

output "primary_key" {
  description = "Cosmos DB primary access key"
  value       = azurerm_cosmosdb_account.this.primary_key
  sensitive   = true
}

output "database_name" {
  description = "Cosmos DB SQL database name"
  value       = azurerm_cosmosdb_sql_database.this.name
}

output "container_names" {
  description = "Map of logical key => container name"
  value       = { for k, v in azurerm_cosmosdb_sql_container.this : k => v.name }
}
