output "account_id" {
  description = "AI Services account resource ID"
  value       = azurerm_cognitive_account.this.id
}

output "account_name" {
  description = "AI Services account name"
  value       = azurerm_cognitive_account.this.name
}

output "endpoint" {
  description = "Azure OpenAI account endpoint (https://<account>.cognitiveservices.azure.com). Use for direct AzureOpenAIClient calls (e.g. embeddings)."
  value       = azurerm_cognitive_account.this.endpoint
}

output "project_id" {
  description = "Resource ID of the default Foundry project."
  value       = azurerm_cognitive_account_project.default.id
}

output "project_name" {
  description = "Name of the default Foundry project."
  value       = azurerm_cognitive_account_project.default.name
}

output "project_endpoint" {
  description = "Foundry project endpoint (https://<account>.services.ai.azure.com/api/projects/<project_name>). Use for AIProjectClient and the agent service."
  # The provider exposes per-API endpoints in `endpoints`. The "AI Foundry API"
  # entry is the project endpoint consumed by Azure.AI.Projects 2.x. We fall
  # back to constructing the URL from the account subdomain for resilience
  # against future endpoint-name changes in the API response.
  value = try(
    azurerm_cognitive_account_project.default.endpoints["AI Foundry API"],
    "https://${azurerm_cognitive_account.this.custom_subdomain_name}.services.ai.azure.com/api/projects/${azurerm_cognitive_account_project.default.name}"
  )
}

output "deployment_name" {
  description = "Name of the deployed chat model"
  value       = azurerm_cognitive_deployment.this.name
}

output "embedding_deployment_name" {
  description = "Name of the deployed embedding model"
  value       = var.create_embedding_deployment ? azurerm_cognitive_deployment.embedding[0].name : null
}

output "primary_access_key" {
  description = "Primary access key for API key authentication (only functional when local_auth_enabled = true)"
  value       = azurerm_cognitive_account.this.primary_access_key
  sensitive   = true
}
