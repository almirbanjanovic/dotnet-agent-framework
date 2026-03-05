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

output "connection_strings" {
  description = "Cosmos DB connection strings"
  value       = azurerm_cosmosdb_account.this.connection_strings
  sensitive   = true
}

output "database_name" {
  description = "Cosmos DB SQL database name"
  value       = azurerm_cosmosdb_sql_database.this.name
}

output "agent_state_container_name" {
  description = "Name of the agent state store container"
  value       = azurerm_cosmosdb_sql_container.agent_state.name
}
