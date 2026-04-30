output "foundry_endpoint" {
  description = "Azure OpenAI endpoint URL"
  value       = module.foundry.endpoint
}

output "chat_deployment_name" {
  description = "Name of the chat model deployment"
  value       = module.foundry.deployment_name
}

output "embedding_deployment_name" {
  description = "Name of the embedding model deployment"
  value       = module.foundry.embedding_deployment_name
}

output "tenant_id" {
  description = "Azure tenant ID (used by DefaultAzureCredential to disambiguate accounts)."
  value       = data.azurerm_client_config.current.tenant_id
}
