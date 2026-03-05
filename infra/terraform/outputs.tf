// Learning/dev convenience outputs.
// Do not expose sensitive values via Terraform outputs in production.

output "openai_endpoint" {
  description = "Azure OpenAI endpoint URL for SDK usage"
  value       = module.openai.endpoint
}

output "openai_api_key" {
  description = "Azure OpenAI API key"
  value       = nonsensitive(module.openai.primary_key)
}

# ---------------------------------------------------------------
# Cosmos DB
# ---------------------------------------------------------------

output "cosmosdb_endpoint" {
  description = "Cosmos DB endpoint URL"
  value       = module.cosmosdb.endpoint
}

output "cosmosdb_account_name" {
  description = "Cosmos DB account name"
  value       = module.cosmosdb.account_name
}

output "cosmosdb_database_name" {
  description = "Cosmos DB database name"
  value       = module.cosmosdb.database_name
}
