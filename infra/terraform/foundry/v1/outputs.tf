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

output "primary_key" {
  description = "Primary access key"
  value       = azurerm_cognitive_account.this.primary_access_key
  sensitive   = true
}

output "deployment_name" {
  description = "Name of the deployed model"
  value       = azurerm_cognitive_deployment.this.name
}
