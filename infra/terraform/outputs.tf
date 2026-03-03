// Learning/dev convenience outputs.
// Do not expose sensitive values via Terraform outputs in production.

output "openai_endpoint" {
  description = "Azure OpenAI endpoint URL for SDK usage"
  value       = "https://${azurerm_cognitive_account.this.custom_subdomain_name}.openai.azure.com/"
}

output "openai_api_key" {
  description = "Azure OpenAI API key"
  value       = nonsensitive(azurerm_cognitive_account.this.primary_access_key)
}
