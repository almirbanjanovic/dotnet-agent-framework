output "account_id" {
  description = "AI Services account resource ID"
  value       = azurerm_cognitive_account.this.id
}

output "account_name" {
  description = "AI Services account name"
  value       = azurerm_cognitive_account.this.name
}

output "endpoint" {
  description = "Azure OpenAI endpoint URL"
  value       = "https://${azurerm_cognitive_account.this.custom_subdomain_name}.openai.azure.com/"
}

output "deployment_name" {
  description = "Name of the deployed chat model"
  value       = azurerm_cognitive_deployment.this.name
}

output "embedding_deployment_name" {
  description = "Name of the deployed embedding model"
  value       = var.create_embedding_deployment ? azurerm_cognitive_deployment.embedding[0].name : null
}
