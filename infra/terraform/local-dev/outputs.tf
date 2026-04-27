output "foundry_endpoint" {
  description = "Azure OpenAI endpoint URL"
  value       = module.foundry.endpoint
}

output "foundry_api_key" {
  description = "API key for local dev authentication"
  value       = module.foundry.primary_access_key
  sensitive   = true
}

output "chat_deployment_name" {
  description = "Name of the chat model deployment"
  value       = module.foundry.deployment_name
}

output "embedding_deployment_name" {
  description = "Name of the embedding model deployment"
  value       = module.foundry.embedding_deployment_name
}
